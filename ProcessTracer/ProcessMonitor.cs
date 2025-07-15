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
            string pipeHandle = pid.ToString() + " " + (Program.CanElevate() ? 0 : 1);

            var si = new STARTUPINFOA
            {
                cb = (uint)Marshal.SizeOf(typeof(STARTUPINFOA))
            };
            var pi = new PROCESS_INFORMATION();
            string appName = options.Executable;
            string commandLine = "\"" + options.Executable + "\" " + options.Arguments;
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProcessTracerCore32.dll");
            Win32.CreationFlag creationFlags =
                Win32.CreationFlag.CREATE_SUSPENDED | Win32.CreationFlag.CREATE_DEFAULT_ERROR_MODE;
            bool result = DetoursLoader.DetourCreateProcessWithDllAWrap(
                appName,
                commandLine,
                IntPtr.Zero, IntPtr.Zero
                , true, (uint)creationFlags,
                IntPtr.Zero, null, ref si, out pi,
                1,
                [
                    dllPath
                ],
                pipeHandle);
            if (!result)
            {
                await logger.LogErrorAsync("Failed to create process with DLL", CancellationToken.None);
                return false;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var needAdminCancellationTokenSource = new CancellationTokenSource();

            int threadCount = Environment.ProcessorCount;
            var tasks = new List<Task>(threadCount);
            TaskManager taskManager = new();
            taskManager.StartTaskExecutor(threadCount, (msg, cancellationToken) =>
            {
                switch (msg.TaskName)
                {
                    case "Log":
                        return logger.LogAsync(msg.LogMessage, cancellationToken);
                    case "Error":
                        return logger.LogErrorAsync(msg.LogMessage, cancellationToken);
                }

                return Task.CompletedTask;
            });
            for (int i = 0; i < threadCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        await using var pipeServer = new NamedPipeServerStream(
                            "ProcessTracerPipe:" + pid,
                            // "ProcessTracerPipe",
                            PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.WriteThrough
                        );
                        await pipeServer.WaitForConnectionAsync(cancellationTokenSource.Token);
                        if (cancellationTokenSource.Token.IsCancellationRequested)
                            return;

                        var reader = new StreamReader(pipeServer);
                        try
                        {
                            while (await reader.ReadLineAsync(cancellationTokenSource.Token) is { } line)
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

                                taskManager.EnqueueTask("Log", "Received: " + line);
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        if (cancellationTokenSource.Token.IsCancellationRequested)
                            return;
                    }
                }, cancellationTokenSource.Token));
            }

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
                Task.WaitAll(tasks.ToArray(), CancellationToken.None);
            }
            catch (AggregateException ex)
            {
                ex.Handle(inner => inner is TaskCanceledException);
            }

            taskManager.StopTaskExecutor();

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