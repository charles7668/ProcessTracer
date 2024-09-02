using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ProcessTracer
{
    internal static class ProcessMonitor
    {
        private static TraceEventSession? _Session;
        private static readonly ConcurrentDictionary<int, Process> _Processes = new();
        private static bool _WaitingAttach;

        private static void StartMonitorProcessExit(Process process)
        {
            Task.Run(async () =>
            {
                do
                {
                    await Task.Delay(1000);
                    foreach ((int key, Process? proc) in _Processes)
                    {
                        try
                        {
                            if (!proc.HasExited) continue;
                            _Processes.Remove(key, out _);
                            Console.WriteLine($"[ProcessExit] Process: {proc.ProcessName}, Process Id: {proc.Id}");
                        }
                        catch
                        {
                            _Processes.Remove(key, out _);
                            Console.WriteLine(
                                $"[ProcessExit] Process: {proc.ProcessName}, Process Id: {proc.Id}");
                        }
                    }
                } while (!_Processes.IsEmpty);

                Console.WriteLine("Process has exited.");
                _Session?.Stop();
            });
        }

        public static void Start(RunOptions options)
        {
            Process? process = null;
            if (options.PID != 0)
            {
                process = Process.GetProcessById(options.PID);
            }
            else if (!string.IsNullOrWhiteSpace(options.ModuleFile))
            {
                _WaitingAttach = true;
                Process[] processes = Process.GetProcesses();
                foreach (Process p in processes)
                {
                    try
                    {
                        if (!string.Equals(p.MainModule?.FileName, options.ModuleFile,
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                        process = p;
                        _WaitingAttach = false;
                        break;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (process == null && !_WaitingAttach)
            {
                Console.Error.WriteLine("Can't find process.");
                Environment.Exit(1);
            }


            if (process is not null)
            {
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
            }

            if (_Session is { IsActive: true })
                _Session.Stop();

            Console.WriteLine("Start monitoring...");

            _Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);

            // enable kernel provider
            _Session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit |
                                          KernelTraceEventParser.Keywords.FileIO |
                                          KernelTraceEventParser.Keywords.Registry);

            _Session.Source.Kernel.FileIOWrite += delegate(FileIOReadWriteTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID) && !string.IsNullOrWhiteSpace(data.FileName))
                    Console.WriteLine(
                        $"[FileIOWrite] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.FileIOCreate += delegate(FileIOCreateTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[FileIOCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.FileIOFileCreate += delegate(FileIONameTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[FileIOFileCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, File: {data.FileName}");
            };
            _Session.Source.Kernel.RegistrySetValue += delegate(RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistrySetValue] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryCreate += delegate(RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryCreate] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryOpen += delegate(RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryOpen] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryDelete += delegate(RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryDelete] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.RegistryDeleteValue += delegate(RegistryTraceData data)
            {
                if (_Processes.ContainsKey(data.ProcessID))
                    Console.WriteLine(
                        $"[RegistryDeleteValue] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Key: {data.KeyName} , ValueName: {data.ValueName}");
            };
            _Session.Source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                if (_WaitingAttach)
                {
                    try
                    {
                        var p = Process.GetProcessById(data.ProcessID);
                        string waitImageName = options.ModuleFile;
                        if (p.MainModule?.FileName != waitImageName) return;
                        _Processes[data.ProcessID] = p;
                        StartMonitorProcessExit(p);
                        _WaitingAttach = false;
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
                else
                {
                    if (!_Processes.ContainsKey(data.ParentID)) return;
                    try
                    {
                        var p = Process.GetProcessById(data.ProcessID);
                        _Processes[data.ProcessID] = p;
                        Console.WriteLine(
                            $"[ProcessStart] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Parent Process Id: {data.ParentID}");
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
            };
            _Session.Source.Kernel.ProcessStop += delegate(ProcessTraceData data)
            {
                if (!_Processes.ContainsKey(data.ProcessID)) return;
                _Processes.Remove(data.ProcessID, out _);
                Console.WriteLine(
                    $"[ProcessStop] Process: {data.ProcessName}, Process Id: {data.ProcessID}");
            };

            if (!_WaitingAttach)
                StartMonitorProcessExit(process);

            _Session.Source.Process();
        }
    }
}