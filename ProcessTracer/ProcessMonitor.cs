using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;

namespace ProcessTracer
{
    internal static class ProcessMonitor
    {
        private static TraceEventSession? _Session;
        private static readonly Dictionary<int, Process> _Processes = new();

        private static void StartMonitorProcessExit(Process process)
        {
            Task.Run(async () =>
            {
                await process.WaitForExitAsync();
                Console.WriteLine("Process has exited.");
                _Session?.Stop();
            });
        }

        public static void Start(RunOptions options)
        {
            var process = Process.GetProcessById(options.PID);
            if (process.HasExited)
            {
                Console.WriteLine("Process has exited.");
                return;
            }

            Console.WriteLine("Process Info : ");
            Console.WriteLine($"  PID : {process.Id}");
            Console.WriteLine($"  Name : {process.ProcessName}");

            List<Process> childProcesses = ProcessHelper.GetChildProcess(process);
            foreach (Process childProcess in childProcesses)
            {
                Console.WriteLine($"    Child Process ID : {childProcess.Id}");
                Console.WriteLine($"    Child Process Name : {childProcess.ProcessName}");
                _Processes[childProcess.Id] = childProcess;
            }

            _Processes[process.Id] = process;

            if (_Session is { IsActive: true })
                _Session.Stop();

            Console.WriteLine("Start monitoring...");

            _Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);

            // enable kernel provider with file IO init and file IO keywords
            _Session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit |
                                          KernelTraceEventParser.Keywords.FileIO);

            if (options.UseFileIOWrite)
            {
                _Session.Source.Kernel.FileIOWrite += delegate (FileIOReadWriteTraceData data)
                {
                    if (_Processes.ContainsKey(data.ProcessID))
                        Console.WriteLine(
                            $"[FileIOWrite] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
                };
            }

            if (options.UseFileIOFileCreate)
            {
                _Session.Source.Kernel.FileIOFileCreate += delegate (FileIONameTraceData data)
                {
                    if (_Processes.ContainsKey(data.ProcessID))
                        Console.WriteLine(
                            $"[FileIOFileCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
                };
            }

            StartMonitorProcessExit(process);

            _Session.Source.Process();
        }
    }
}