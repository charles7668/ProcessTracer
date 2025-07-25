using CommandLine;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace ProcessTracer
{
    internal static class Program
    {
        private static readonly List<string> _OriginalArgs = [];
        private static MemoryMappedFile? _ArgsMmf;

        public static int ChildPid { get; set; }

        private static void Main(string[] args)
        {
            try
            {
                InitializeApplication();
                ProcessArguments(args);
                ParseAndExecute(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Application error: {ex.Message}");
                Environment.Exit(1);
            }
            finally
            {
                CleanupResources();
            }
        }

        private static void InitializeApplication()
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        private static void ProcessArguments(string[] args)
        {
            foreach (string arg in args)
            {
                Console.WriteLine(arg);
                string escapedArg = arg.Replace("\"", "\\\"");
                _OriginalArgs.Add($"\"{escapedArg}\"");
            }
        }

        private static void ParseAndExecute(string[] args)
        {
            Parser.Default.ParseArguments<RunOptions>(args)
                .WithParsed(StartMonitor)
                .WithNotParsed(HandleParseError);
        }

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            foreach (Error error in errors)
                Console.Error.WriteLine(error.ToString());

            Environment.Exit(1);
        }

        private static void StartMonitor(RunOptions options)
        {
            var validator = new OptionsValidator();
            if (!validator.ValidateOptions(options))
                return;

            var appContext = new ApplicationContext(options);

            try
            {
                appContext.Initialize();

                if (options.RunAs && CanElevate())
                {
                    ExecuteElevatedWorkflow(appContext);
                }
                else
                {
                    ExecuteNormalWorkflow(appContext);
                }
            }
            finally
            {
                appContext.Dispose();
            }
        }

        private static void ExecuteElevatedWorkflow(ApplicationContext context)
        {
            var elevationHandler = new ElevationHandler(context, _OriginalArgs);
            elevationHandler.WaitForElevation();
        }

        private static void ExecuteNormalWorkflow(ApplicationContext context)
        {
            using var monitor = new ProcessMonitor(context.Options, context.Logger);
            bool needRestart = monitor.Start().ConfigureAwait(false).GetAwaiter().GetResult();

            if (needRestart && CanElevate())
            {
                var elevationHandler = new ElevationHandler(context, _OriginalArgs);
                elevationHandler.WaitForElevation();
            }
        }

        public static unsafe bool CanElevate()
        {
            HANDLE hToken = HANDLE.Null;
            TOKEN_ELEVATION_TYPE tokenType = TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;

            try
            {
                if (!PInvoke.OpenProcessToken(
                        (HANDLE)Process.GetCurrentProcess().Handle,
                        TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS,
                        &hToken))
                {
                    return false;
                }

                using var safeHandle = new SafeHandleWrapper(hToken.Value);
                PInvoke.GetTokenInformation(
                    safeHandle,
                    TOKEN_INFORMATION_CLASS.TokenElevationType,
                    &tokenType,
                    sizeof(TOKEN_ELEVATION_TYPE),
                    out uint _);

                return tokenType == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
            }
            finally
            {
                if (hToken != HANDLE.Null)
                    PInvoke.CloseHandle(hToken);
            }
        }

        private static void CleanupResources()
        {
            _ArgsMmf?.Dispose();
        }

        // 支援類別
        private sealed class OptionsValidator
        {
            public bool ValidateOptions(RunOptions options)
            {
                if (!string.IsNullOrEmpty(options.OutputFile) &&
                    !string.IsNullOrEmpty(options.OutputErrorFilePath))
                {
                    string fullOutputFile = Path.GetFullPath(options.OutputFile);
                    string fullErrorFile = Path.GetFullPath(options.OutputErrorFilePath);

                    if (fullOutputFile == fullErrorFile)
                    {
                        Console.Error.WriteLine("Output and error files must be different.");
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class ApplicationContext(RunOptions options) : IDisposable
        {
            public RunOptions Options { get; } = options;
            public Logger Logger { get; private set; } = null!;
            public int CurrentProcessId { get; } = Process.GetCurrentProcess().Id;

            public void Dispose()
            {
                Logger.Dispose();
                _ArgsMmf?.Dispose();
            }

            public void Initialize()
            {
                CreateMemoryMappedFile();
                ConfigureConsoleVisibility();
                InitializeLogger();
                LogChildProcessIfNeeded();
            }

            private void CreateMemoryMappedFile()
            {
                string mmfName = $"ProcessTracerArgs:{CurrentProcessId}";
                Console.WriteLine($@"Create Map File: Local\{mmfName}");

                _ArgsMmf = MemoryMappedFile.CreateNew(mmfName, 1024, MemoryMappedFileAccess.ReadWrite);

                using MemoryMappedViewAccessor accessor = _ArgsMmf.CreateViewAccessor();
                string message = $"--parent {CurrentProcessId}";
                byte[] data = Encoding.UTF8.GetBytes(message);

                accessor.Write(0, message.Length);
                accessor.WriteArray(sizeof(int), data, 0, data.Length);
            }

            private void ConfigureConsoleVisibility()
            {
                if (Options.HideConsole)
                {
                    var consoleManager = new ConsoleManager();
                    consoleManager.HideConsole();
                }
            }

            private void InitializeLogger()
            {
                Logger = new Logger(Options);
            }

            private void LogChildProcessIfNeeded()
            {
                if (Options.Parent != 0)
                {
                    Logger.Log($"[ChildProcess] {CurrentProcessId}");
                }
            }
        }

        private sealed class ConsoleManager
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr GetConsoleWindow();

            [DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            public void HideConsole()
            {
                IntPtr hWnd = GetConsoleWindow();
                ShowWindow(hWnd, 0);
            }
        }

        private sealed class ElevationHandler(ApplicationContext context, List<string> originalArgs)
        {
            public void WaitForElevation()
            {
                using var cts = new CancellationTokenSource();

                Task elevateTask = CreateElevationTask(cts);
                Task pipeTask = CreatePipeMonitoringTask(cts);

                Task.WaitAll([elevateTask, pipeTask], CancellationToken.None);
            }

            private Task CreateElevationTask(CancellationTokenSource cts)
            {
                return Task.Factory.StartNew(() =>
                    {
                        RunElevated(originalArgs.ToArray());
                    }, TaskCreationOptions.LongRunning)
                    .ContinueWith(_ => cts.Cancel(), CancellationToken.None);
            }

            private Task CreatePipeMonitoringTask(CancellationTokenSource cts)
            {
                return TaskExecutor.StartNamedPipeReceiveTaskAsync(
                    $"ProcessTracerPipe:{context.CurrentProcessId}",
                    context.Logger,
                    ProcessPipeMessage,
                    cts.Token);
            }

            private Task<bool> ProcessPipeMessage(string line)
            {
                if (line == "[CloseApp]")
                {
                    HandleCloseAppMessage();
                }
                else if (line.StartsWith("[ChildProcess] "))
                {
                    HandleChildProcessMessage(line);
                }

                return Task.FromResult(true);
            }

            private void HandleCloseAppMessage()
            {
                var stopSignalWriter = new StopSignalWriter();
                stopSignalWriter.WriteStopSignal(ChildPid);
            }

            private static void HandleChildProcessMessage(string line)
            {
                const string prefix = "[ChildProcess] ";
                string childPidString = line.Substring(prefix.Length);
                if (int.TryParse(childPidString, out int childPid))
                {
                    ChildPid = childPid;
                }
            }

            private static void RunElevated(string[] args)
            {
                var processStarter = new ElevatedProcessStarter();
                processStarter.StartElevated(args);
            }
        }

        private sealed class StopSignalWriter
        {
            public void WriteStopSignal(int targetPid)
            {
                Console.WriteLine($@"Try write stop signal to file, Local\ProcessTracerMapFile:{targetPid}");

                try
                {
                    using var stopRequestMappedFile = MemoryMappedFile.CreateNew(
                        $"Local\\ProcessTracerMapFile:{targetPid}",
                        4,
                        MemoryMappedFileAccess.ReadWrite);

                    using MemoryMappedViewAccessor accessor = stopRequestMappedFile.CreateViewAccessor();
                    string message = "stop";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    accessor.Write(0, data.Length);
                    accessor.WriteArray(sizeof(int), data, 0, data.Length);

                    Console.WriteLine(@"Write stop signal success");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Failed to write stop signal to Local\\ProcessTracerMapFile: {ex.Message}");
                }
            }
        }

        private sealed class ElevatedProcessStarter
        {
            public void StartElevated(string[] args)
            {
                var newArgs = new List<string>(args)
                {
                    $"--parent {Process.GetCurrentProcess().Id}"
                };

                var elevationInfo = new ProcessStartInfo
                {
                    FileName = "launcher.exe",
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", newArgs),
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                try
                {
                    using var proc = Process.Start(elevationInfo);
                    proc?.WaitForExit();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Process can't start with admin rights: {ex.Message}");
                    Environment.Exit(1);
                }
            }
        }
    }
}