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

        private static Logger _Logger = null!;

        public static int ChildPid;

        public static unsafe bool CanElevate()
        {
            HANDLE hToken = HANDLE.Null;
            TOKEN_ELEVATION_TYPE tokenType = TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
            if (!PInvoke.OpenProcessToken((HANDLE)Process.GetCurrentProcess().Handle,
                    TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS,
                    &hToken)) return false;
            SafeHandleWrapper safeHandle = new(hToken.Value);
            PInvoke.GetTokenInformation(safeHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, &tokenType,
                sizeof(TOKEN_ELEVATION_TYPE), out uint _);
            PInvoke.CloseHandle(hToken);

            return tokenType == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            foreach (Error error in errors)
                Console.Error.WriteLine(error.ToString());

            Environment.Exit(1);
        }

        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            foreach (string s in args)
            {
                Console.WriteLine(s);
                string temp = s.Replace("\"", "\\\"");
                _OriginalArgs.Add("\"" + temp + "\"");
            }

            Parser.Default.ParseArguments<RunOptions>(args)
                .WithParsed(StartMonitor)
                .WithNotParsed(HandleParseError);
        }

        private static void RunElevate(string[] args, RunOptions options)
        {
            var newArgs = new List<string>(args)
            {
                "--parent " + Process.GetCurrentProcess().Id
            };
            ProcessStartInfo elevationInfo = new()
            {
                FileName = "launcher.exe",
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", newArgs),
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            try
            {
                var proc = Process.Start(elevationInfo);
                proc?.WaitForExit();
                // Environment.Exit(0);
            }
            catch
            {
                Console.Error.WriteLine("process can't start with admin rights");
                Environment.Exit(1);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static MemoryMappedFile _argsMMF;

        private static void StartMonitor(RunOptions options)
        {
            if (!string.IsNullOrEmpty(options.OutputFile) && !string.IsNullOrEmpty(options.OutputErrorFilePath))
            {
                string fullOutputFile = Path.GetFullPath(options.OutputFile);
                string fullErrorFile = Path.GetFullPath(options.OutputErrorFilePath);
                if (fullOutputFile == fullErrorFile)
                {
                    Console.Error.WriteLine("Output and error files must be different.");
                    return;
                }
            }

            Console.WriteLine(@"Create Map File : " + @"Local\ProcessTracerArgs:" +
                              Process.GetCurrentProcess().Id);
            _argsMMF = MemoryMappedFile.CreateNew("ProcessTracerArgs:" + Process.GetCurrentProcess().Id, 1024,
                MemoryMappedFileAccess.ReadWrite);
            using (MemoryMappedViewAccessor accessor = _argsMMF.CreateViewAccessor())
            {
                List<string> newArgs = ["--parent " + Process.GetCurrentProcess().Id];
                string message = string.Join(" ", newArgs);
                accessor.Write(0, message.Length);
                byte[] data = Encoding.UTF8.GetBytes(message);
                accessor.WriteArray(sizeof(int), data, 0, data.Length);
            }

            if (options.HideConsole)
            {
                IntPtr hWnd = GetConsoleWindow();
                ShowWindow(hWnd, 0);
            }

            _Logger = new Logger(options);

            if (options.Parent != 0)
                _Logger.Log("[ChildProcess] " + Process.GetCurrentProcess().Id);

            void WaitElevate()
            {
                var cts = new CancellationTokenSource();
                Task elevateTask = Task.Factory.StartNew(() =>
                {
                    RunElevate(_OriginalArgs.ToArray(), options);
                }, TaskCreationOptions.LongRunning).ContinueWith(_ => cts.Cancel(), CancellationToken.None);
                TaskExecutor.StartNamedPipeReceiveTaskAsync(
                    "ProcessTracerPipe:" + Process.GetCurrentProcess().Id,
                    _Logger,
                    cts.Token, line =>
                    {
                        if (line == "[CloseApp]")
                        {
                            Console.WriteLine(@"Try write stop signal to file , Global\\ProcessTracerMapFile:" +
                                              ChildPid);
                            try
                            {
                                var mmfile = MemoryMappedFile.CreateNew("Local\\ProcessTracerMapFile:" + ChildPid, 4,
                                    MemoryMappedFileAccess.ReadWrite);
                                using (MemoryMappedViewAccessor accessor = mmfile.CreateViewAccessor())
                                {
                                    string message = "stop";
                                    byte[] data = Encoding.UTF8.GetBytes(message);
                                    accessor.Write(0, data.Length);
                                    accessor.WriteArray(sizeof(int), data, 0, data.Length);
                                }

                                Console.WriteLine(@"Write stop signal success");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine(
                                    "Failed to write stop signal to Local\\ProcessTracerMapFile: " + ex.Message);
                            }
                        }
                        else if (line.StartsWith("[ChildProcess] "))
                        {
                            string childPidString = line.Substring("[ChildProcess] ".Length);
                            ChildPid = int.Parse(childPidString);
                        }

                        return Task.FromResult(true);
                    }).ConfigureAwait(false).GetAwaiter().GetResult();
                elevateTask.Wait(CancellationToken.None);
            }

            if (options.RunAs && CanElevate())
            {
                WaitElevate();
            }
            else
            {
                ProcessMonitor monitor = new(options, _Logger);
                bool needRestart = monitor.Start().ConfigureAwait(false).GetAwaiter().GetResult();
                if (needRestart && CanElevate())
                {
                    WaitElevate();
                }
            }
        }
    }
}