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

        private static void StartMonitorProcessExit (Process process)
        {
            Task.Run(async () =>
            {
                await process.WaitForExitAsync();
                Console.WriteLine("Process has exited.");
                _Session?.Stop();
            });
        }

        public static void Start (RunOptions options)
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

            // enable kernel provider
            _Session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit |
                                          KernelTraceEventParser.Keywords.FileIO |
                                          KernelTraceEventParser.Keywords.Registry);

            _Session.Source.Kernel.FileIOWrite += delegate (FileIOReadWriteTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID) && !string.IsNullOrWhiteSpace(data.FileName))
                    Console.WriteLine(
                        $"[FileIOWrite] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.FileIOCreate += delegate (FileIOCreateTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[FileIOCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.FileIOFileCreate += delegate (FileIONameTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[FileIOFileCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.RegistrySetValue += delegate (RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistrySetValue] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryCreate += delegate (RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryOpen += delegate (RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryOpen] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryDelete += delegate (RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryDelete] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryDeleteValue += delegate (RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryDeleteValue] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.ProcessStart += delegate (ProcessTraceData data)
            {
                if (_Processes.ContainsKey(ProcessHelper.GetParentProcessId(data.ProcessID)))
                {
                    Console.WriteLine(
                        $"[ProcessStart] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Parent Process Id: {data.ParentID}");
                }
            };
            _Session.Source.Kernel.ProcessStop += delegate (ProcessTraceData data)
            {
                if (!_Processes.ContainsKey(data.ProcessID)) return;
                _Processes.Remove(data.ProcessID);
                Console.WriteLine(
                    $"[ProcessStop] Process: {data.ProcessName}, Process Id: {data.ProcessID}");
            };

            StartMonitorProcessExit(process);

            _Session.Source.Process();
        }
    }
}