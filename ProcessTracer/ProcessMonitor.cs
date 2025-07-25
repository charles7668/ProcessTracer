using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace ProcessTracer
{
    public class ProcessMonitor : IAsyncDisposable
    {
        public ProcessMonitor(RunOptions options, Logger logger)
        {
            _options = options;
            _logger = logger;
            _currentProcessIdString = Process.GetCurrentProcess().Id.ToString();
            _hookInfoListenPipeName = "ProcessTracerPipe:" + _currentProcessIdString;
            _stopRequestMappedFileName = $"Local\\ProcessTracerMapFile:{_currentProcessIdString}";
        }

        private readonly string _currentProcessIdString;
        private readonly string _hookInfoListenPipeName;
        private readonly Logger _logger;
        private readonly RunOptions _options;
        private readonly List<int> _reusableRemoveList = new(16);
        private readonly string _stopRequestMappedFileName;
        private readonly ConcurrentDictionary<int, Process> _trackProcesses = [];

        public ValueTask DisposeAsync()
        {
            foreach (Process process in _trackProcesses.Values)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // ignore
                }
            }

            _trackProcesses.Clear();
            return ValueTask.CompletedTask;
        }

        public async Task<bool> Start()
        {
            if (!CreateInjectedProcess(out PROCESS_INFORMATION pi))
            {
                return await HandleProcessCreationFailure();
            }

            AddProcessToMonitor((int)pi.dwProcessId);

            using MonitoringContext context = CreateMonitoringContext();
            MonitoringTasks tasks = await StartMonitoringTasks(context);

            PInvoke.ResumeThread(pi.hThread);

            return await WaitForCompletionAndCleanup(tasks, context);
        }

        private async Task<bool> HandleProcessCreationFailure()
        {
            bool permissionRequest = DetoursLoader.GetDetourCreateProcessError() == 740;
            if (!permissionRequest)
                await _logger.LogErrorAsync("Failed to create process with DLL", CancellationToken.None);
            return permissionRequest;
        }

        private MonitoringContext CreateMonitoringContext()
        {
            return new MonitoringContext
            {
                StopSignal = false,
                WaitChild = false
            };
        }

        private Task<MonitoringTasks> StartMonitoringTasks(MonitoringContext context)
        {
            var messageProcessor = new MessageProcessor(this, context);

            Task loggingTask = TaskExecutor.StartNamedPipeReceiveTaskAsync(
                _hookInfoListenPipeName,
                _logger,
                messageProcessor.ProcessMessage,
                context.CancellationTokenSource.Token);

            var memoryMonitor = new MemoryMappedFileMonitor(_stopRequestMappedFileName, _logger, context);
            Task stopSignalTask = memoryMonitor.StartMonitoring();

            Task processMonitoringTask = StartProcessMonitoring(context);

            return Task.FromResult(new MonitoringTasks(loggingTask, stopSignalTask, processMonitoringTask));
        }

        private Task StartProcessMonitoring(MonitoringContext context)
        {
            return Task.Run(async () =>
            {
                while (!context.OverallStopToken.IsCancellationRequested)
                {
                    if (await ShouldExitMonitoring(context))
                        break;

                    CleanupExitedProcesses();
                    await Task.Delay(200, context.OverallStopToken);
                }
            }, context.OverallStopToken);
        }

        private async Task<bool> ShouldExitMonitoring(MonitoringContext context)
        {
            if (!context.WaitChild && _trackProcesses.IsEmpty)
            {
                await Task.Delay(500, context.OverallStopToken);
                return !context.WaitChild && _trackProcesses.IsEmpty;
            }

            return false;
        }

        private void CleanupExitedProcesses()
        {
            _reusableRemoveList.Clear();

            foreach (KeyValuePair<int, Process> trackProcess in _trackProcesses)
            {
                try
                {
                    if (trackProcess.Value.HasExited)
                        _reusableRemoveList.Add(trackProcess.Key);
                }
                catch
                {
                    _reusableRemoveList.Add(trackProcess.Key);
                }
            }

            foreach (int pid in _reusableRemoveList)
                RemoveProcessFromMonitor(pid);
        }

        private async Task<bool> WaitForCompletionAndCleanup(MonitoringTasks tasks, MonitoringContext context)
        {
            try
            {
                await Task.WhenAny(tasks.LoggingTask, tasks.StopSignalTask, tasks.ProcessMonitoringTask);
            }
            catch (OperationCanceledException)
            {
            }

            await context.CancellationTokenSource.CancelAsync();

            try
            {
                await tasks.LoggingTask;
            }
            catch (OperationCanceledException) { }

            return HandleFinalCleanup(context);
        }

        private bool HandleFinalCleanup(MonitoringContext context)
        {
            if (context.NeedAdminCancellationTokenSource.IsCancellationRequested || context.StopSignal)
            {
                KillAllTrackedProcesses();
                return !context.StopSignal;
            }

            return false;
        }

        private void KillAllTrackedProcesses()
        {
            foreach (KeyValuePair<int, Process> trackProcess in _trackProcesses.Reverse())
            {
                try
                {
                    if (!trackProcess.Value.HasExited)
                        trackProcess.Value.Kill();
                }
                catch
                {
                    // ignore
                }
            }
        }

        private void AddProcessToMonitor(int pid)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                _trackProcesses.TryAdd(pid, proc);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to add process {pid} to monitor: {ex.Message}");
            }
        }

        private void RemoveProcessFromMonitor(int pid)
        {
            if (!_trackProcesses.TryRemove(pid, out Process? process))
                return;
            try
            {
                process.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private bool CreateInjectedProcess(out PROCESS_INFORMATION processInfo)
        {
            var processCreator = new InjectedProcessCreator(_options, _currentProcessIdString);
            return processCreator.CreateProcess(out processInfo);
        }

        private sealed class MonitoringContext : IDisposable
        {
            public CancellationTokenSource CancellationTokenSource { get; } = new();
            public CancellationTokenSource NeedAdminCancellationTokenSource { get; } = new();
            public bool StopSignal { get; set; }
            public bool WaitChild { get; set; }

            public CancellationToken OverallStopToken =>
                CancellationTokenSource.CreateLinkedTokenSource(
                    CancellationTokenSource.Token,
                    NeedAdminCancellationTokenSource.Token).Token;

            public void Dispose()
            {
                CancellationTokenSource.Dispose();
                NeedAdminCancellationTokenSource.Dispose();
            }
        }

        private sealed class MessageProcessor(ProcessMonitor monitor, MonitoringContext context)
        {
            private const string SHELL_EXECUTE_START_HOOK = "[Hook] ShellExecuteExW [Info] Start HookShellExecuteW";
            private const string SHELL_EXECUTE_ERROR_HOOK = "[Hook] ShellExecuteExW [Info] Error HookShellExecuteW";
            private const string PERMISSION_REQUEST = "[Info] Permission Request";

            private const string CREATE_PROCESS_HOOK_PREFIX =
                "[Hook] CreateProcessInternalW Process created successfully with PID: ";

            private const string EXIT_PROCESS_HOOK_PREFIX = "[Hook] ExitProcess ";
            private const string CHILD_PROCESS_PREFIX = "[ChildProcess] ";

            public async Task<bool> ProcessMessage(string line)
            {
                if (line == "[CloseApp]")
                    return await HandleCloseApp();

                if (line.StartsWith(CHILD_PROCESS_PREFIX))
                    return HandleChildProcess(line);

                int firstSpaceIndex = line.IndexOf(' ');
                if (firstSpaceIndex == -1)
                    return true;

                string checkLine = line.Substring(firstSpaceIndex + 1);
                return await ProcessLogMessage(checkLine);
            }

            private async Task<bool> HandleCloseApp()
            {
                context.StopSignal = true;
                await context.CancellationTokenSource.CancelAsync();
                return true;
            }

            private bool HandleChildProcess(string line)
            {
                context.WaitChild = false;
                string childPidString = line.Substring(CHILD_PROCESS_PREFIX.Length);
                if (int.TryParse(childPidString, out int childPid))
                {
                    Program.ChildPid = childPid;
                    monitor.AddProcessToMonitor(childPid);
                }

                return true;
            }

            private async Task<bool> ProcessLogMessage(string checkLine)
            {
                if (checkLine == SHELL_EXECUTE_START_HOOK)
                {
                    context.WaitChild = true;
                }
                else if (checkLine == SHELL_EXECUTE_ERROR_HOOK)
                {
                    context.WaitChild = false;
                }
                else if (checkLine == PERMISSION_REQUEST)
                {
                    await context.NeedAdminCancellationTokenSource.CancelAsync();
                }
                else if (checkLine.StartsWith(CREATE_PROCESS_HOOK_PREFIX))
                {
                    HandleCreateProcess(checkLine);
                }
                else if (checkLine.StartsWith(EXIT_PROCESS_HOOK_PREFIX))
                {
                    HandleExitProcess(checkLine);
                }

                return true;
            }

            private void HandleCreateProcess(string checkLine)
            {
                string pidString = checkLine.Substring(CREATE_PROCESS_HOOK_PREFIX.Length);
                if (int.TryParse(pidString, out int newProcId))
                {
                    monitor.AddProcessToMonitor(newProcId);
                }
            }

            private void HandleExitProcess(string checkLine)
            {
                string exitInfoString = checkLine.Substring(EXIT_PROCESS_HOOK_PREFIX.Length);
                int spaceIndex = exitInfoString.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    string pidString = exitInfoString.Substring(0, spaceIndex);
                    if (int.TryParse(pidString, out int pid))
                    {
                        monitor.RemoveProcessFromMonitor(pid);
                    }
                }
            }
        }

        private sealed class MemoryMappedFileMonitor(string fileName, Logger logger, MonitoringContext context)
        {
            public Task StartMonitoring()
            {
                return Task.Factory.StartNew(async () =>
                {
                    await logger.LogAsync($"Monitoring MemoryMappedFile: {fileName}", CancellationToken.None);

                    int delayMs = 50;
                    const int maxDelayMs = 500;

                    while (!context.CancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            if (await TryReadStopSignal())
                            {
                                context.StopSignal = true;
                                await context.CancellationTokenSource.CancelAsync();
                                return;
                            }

                            delayMs = 50;
                        }
                        catch (FileNotFoundException)
                        {
                            delayMs = Math.Min(delayMs * 2, maxDelayMs);
                        }
                        catch (Exception ex)
                        {
                            await logger.LogErrorAsync($"Error reading memory mapped file: {ex.Message}",
                                CancellationToken.None);
                            delayMs = Math.Min(delayMs * 2, maxDelayMs);
                        }

                        try
                        {
                            await Task.Delay(delayMs, context.CancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }, TaskCreationOptions.LongRunning).Unwrap();
            }

            private async Task<bool> TryReadStopSignal()
            {
                using var mmf = MemoryMappedFile.OpenExisting(fileName);
                using MemoryMappedViewAccessor accessor =
                    mmf.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Read);

                int length = accessor.ReadInt32(0);
                if (length > 0)
                {
                    await logger.LogAsync($"Stop signal received from {fileName}, length: {length}",
                        CancellationToken.None);
                    return true;
                }

                return false;
            }
        }

        private sealed class InjectedProcessCreator(RunOptions options, string currentProcessId)
        {
            public bool CreateProcess(out PROCESS_INFORMATION processInfo)
            {
                byte[] pipeHandle =
                    Encoding.Default.GetBytes(currentProcessId + " " + (Program.CanElevate() ? 0 : 1) + "\0");

                var si = new STARTUPINFOW
                {
                    cb = (uint)Marshal.SizeOf(typeof(STARTUPINFOW))
                };
                processInfo = new PROCESS_INFORMATION();

                string appName = options.Executable;
                byte[] appNameBytes = Encoding.Unicode.GetBytes(appName + "\0");
                string commandLine = $"\"{options.Executable}\" {options.Arguments}";
                byte[] commandLineBytes = Encoding.Unicode.GetBytes(commandLine + "\0");
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProcessTracerCore32.dll");

                Console.WriteLine($@"Dll path: {dllPath}");
                Console.WriteLine($@"Command Line: {commandLine}");

                Win32.CreationFlag creationFlags =
                    Win32.CreationFlag.CREATE_SUSPENDED | Win32.CreationFlag.CREATE_DEFAULT_ERROR_MODE;

                return ExecuteProcessCreation(appNameBytes, commandLineBytes, dllPath, creationFlags, ref si,
                    out processInfo, pipeHandle);
            }

            private static bool ExecuteProcessCreation(byte[] appNameBytes, byte[] commandLineBytes, string dllPath,
                Win32.CreationFlag creationFlags, ref STARTUPINFOW si, out PROCESS_INFORMATION processInfo,
                byte[] pipeHandle)
            {
                IntPtr strPtr = IntPtr.Zero;
                IntPtr dllArray = IntPtr.Zero;

                try
                {
                    byte[] ansiDllBytes = Encoding.Default.GetBytes(dllPath + '\0');
                    strPtr = Marshal.AllocHGlobal(ansiDllBytes.Length);
                    Marshal.Copy(ansiDllBytes, 0, strPtr, ansiDllBytes.Length);

                    dllArray = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(dllArray, 0, strPtr);

                    return DetoursLoader.DetourCreateProcessWithDllWWrap(
                        appNameBytes, commandLineBytes, IntPtr.Zero, IntPtr.Zero,
                        true, (uint)creationFlags, IntPtr.Zero, null,
                        ref si, out processInfo, 1, dllArray, pipeHandle);
                }
                finally
                {
                    if (strPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(strPtr);
                    if (dllArray != IntPtr.Zero)
                        Marshal.FreeHGlobal(dllArray);
                }
            }
        }

        private sealed record MonitoringTasks(Task LoggingTask, Task StopSignalTask, Task ProcessMonitoringTask);
    }
}