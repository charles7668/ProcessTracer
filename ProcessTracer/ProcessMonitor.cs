using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace ProcessTracer
{
    public class ProcessMonitor(RunOptions options, Logger logger)
    {
        private readonly ConcurrentDictionary<int, Process> _trackProcesses = [];

        private void AddProcessToMonitor(int pid)
        {
            _trackProcesses.TryAdd(pid, Process.GetProcessById(pid));
        }

        private void RemoveProcessFromMonitor(int pid)
        {
            _trackProcesses.TryRemove(pid, out _);
        }

        public async Task<bool> Start()
        {
            int pid = Process.GetCurrentProcess().Id;
            byte[] pipeHandle = Encoding.Default.GetBytes(pid.ToString() + " " + (Program.CanElevate() ? 0 : 1) + "\0");

            var si = new STARTUPINFOW
            {
                cb = (uint)Marshal.SizeOf(typeof(STARTUPINFOW))
            };
            var pi = new PROCESS_INFORMATION();
            string appName = options.Executable;
            byte[] appNameBytes = Encoding.Unicode.GetBytes(appName + "\0");
            string commandLine = "\"" + options.Executable + "\" " + options.Arguments;
            byte[] commandLineBytes = Encoding.Unicode.GetBytes(commandLine + "\0");
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProcessTracerCore32.dll");
            Win32.CreationFlag creationFlags =
                Win32.CreationFlag.CREATE_SUSPENDED | Win32.CreationFlag.CREATE_DEFAULT_ERROR_MODE;
            byte[] ansiDllBytes = Encoding.Default.GetBytes(dllPath + '\0');
            IntPtr strPtr = Marshal.AllocHGlobal(ansiDllBytes.Length);
            Marshal.Copy(ansiDllBytes, 0, strPtr, ansiDllBytes.Length);
            var dllPtrs = new List<IntPtr>
                { strPtr };
            IntPtr dllArray = Marshal.AllocHGlobal(IntPtr.Size * dllPtrs.Count);
            for (int i = 0; i < dllPtrs.Count; i++)
            {
                Marshal.WriteIntPtr(dllArray, i * IntPtr.Size, dllPtrs[i]);
            }
            Console.WriteLine("Command Line : " + commandLine);
            bool result = DetoursLoader.DetourCreateProcessWithDllWWrap(
                appNameBytes,
                commandLineBytes,
                IntPtr.Zero, IntPtr.Zero
                , true, (uint)creationFlags,
                IntPtr.Zero, null, ref si, out pi,
                1,
                dllArray,
                pipeHandle);
            foreach (var ptr in dllPtrs)
                Marshal.FreeHGlobal(ptr);
            Marshal.FreeHGlobal(dllArray);
            if (!result)
            {
                var error = DetoursLoader.GetDetourCreateProcessError();
                if (error == 740)
                {
                    return true;
                }
                await logger.LogErrorAsync("Failed to create process with DLL", CancellationToken.None);
                return false;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var needAdminCancellationTokenSource = new CancellationTokenSource();

            Task loggingTask = TaskExecutor.StartNamedPipeReceiveTaskAsync("ProcessTracerPipe:" + pid, logger,
                cancellationTokenSource.Token, async (line) =>
                {
                    var lines = line.Split(' ');
                    var checkLine = string.Join(" ", lines[1..]);
                    if (checkLine == "[Info] Permission Request")
                    {
                        await needAdminCancellationTokenSource.CancelAsync();
                    }
                    else if (checkLine.StartsWith(
                                 "[Hook] CreateProcessInternalW Process created successfully with PID: "))
                    {
                        string s = checkLine.Replace(
                            "[Hook] CreateProcessInternalW Process created successfully with PID: ", "");
                        AddProcessToMonitor(Convert.ToInt32(s));
                    }
                    else if (checkLine.StartsWith("[Hook] ExitProcess "))
                    {
                        string s = checkLine.Replace("[Hook] ExitProcess ", "").Split(' ')[0];
                        RemoveProcessFromMonitor(Convert.ToInt32(s));
                    }

                    return true;
                });

            AddProcessToMonitor((int)pi.dwProcessId);
            PInvoke.ResumeThread(pi.hThread);
            try
            {
                await Task.Run(async () =>
                {
                    while (_trackProcesses.Count > 0 && !needAdminCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await Task.Delay(100, needAdminCancellationTokenSource.Token);
                        List<int> removePending = [];
                        foreach (KeyValuePair<int, Process> trackProcess in _trackProcesses)
                        {
                            if (trackProcess.Value.HasExited)
                            {
                                removePending.Add(trackProcess.Key);
                            }
                        }

                        foreach (int i in removePending)
                        {
                            RemoveProcessFromMonitor(i);
                        }
                    }
                }, needAdminCancellationTokenSource.Token);
            }
            catch (TaskCanceledException ex)
            {
                // ignore
            }

            await cancellationTokenSource.CancelAsync();
            try
            {
                await loggingTask;
                Console.WriteLine("Logging task completed.");
            }
            catch (AggregateException ex)
            {
                ex.Handle(inner => inner is TaskCanceledException);
            }

            if (needAdminCancellationTokenSource.IsCancellationRequested)
            {
                foreach (KeyValuePair<int, Process> trackProcess in _trackProcesses)
                {
                    if (!trackProcess.Value.HasExited)
                        trackProcess.Value.Kill();
                }

                return true;
            }

            return false;
        }
    }
}