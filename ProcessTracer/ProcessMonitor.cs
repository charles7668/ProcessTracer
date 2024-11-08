﻿using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;

namespace ProcessTracer
{
    internal static class ProcessMonitor
    {
        private static TraceEventSession? _Session;
        private static readonly ConcurrentDictionary<int, Process> _Processes = new();
        private static bool _WaitingAttach;
        private static Task? _MonitorTask;

        private static void StartMonitorProcessExit(int waitingTime)
        {
            if (_MonitorTask != null)
                return;
            DateTime startTime = DateTime.Now;
            _MonitorTask = Task.Run(async () =>
            {
                do
                {
                    if (_WaitingAttach)
                    {
                        DateTime now = DateTime.Now;
                        if (waitingTime > 0 && (now - startTime).TotalMilliseconds >= waitingTime)
                        {
                            Console.WriteLine("Timeout exceeded");
                            _Session?.Stop();
                            break;
                        }

                        continue;
                    }

                    await Task.Delay(1000);
                    foreach ((int key, Process? proc) in _Processes)
                    {
                        try
                        {
                            if (!proc.HasExited) continue;
                            _Processes.Remove(key, out _);
                            Console.WriteLine($"[ProcessExit] Process Id: {key}");
                        }
                        catch
                        {
                            _Processes.Remove(key, out _);
                            Console.WriteLine(
                                $"[ProcessExit] Process Id: {key}");
                        }
                    }

                    if (!_Processes.IsEmpty)
                        continue;
                    Console.WriteLine("Process has exited.");
                    while (_Session == null)
                    {
                    }

                    _Session.Stop();
                    break;
                } while (true);
            });
        }

        public static void Start(RunOptions options)
        {
            // first check if PID is provided
            Dictionary<int, List<ProcessInfo>> processParentIdDict = [];
            if (options.PID != 0)
            {
                const string query = "SELECT ProcessId, Name, ParentProcessId , ExecutablePath FROM Win32_Process";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementBaseObject? o in searcher.Get())
                    {
                        if (o == null)
                            continue;
                        ManagementBaseObject p = o;
                        int processId = Convert.ToInt32(p["ProcessId"]);
                        string? processName = p["Name"].ToString();
                        int parentProcessId = Convert.ToInt32(p["ParentProcessId"]);
                        string? executablePath = p["ExecutablePath"]?.ToString();
                        if (processName == null)
                            continue;

                        if (!processParentIdDict.ContainsKey(parentProcessId))
                            processParentIdDict[parentProcessId] = new List<ProcessInfo>();
                        processParentIdDict[parentProcessId].Add(new ProcessInfo(processName, processId,
                            parentProcessId, executablePath));
                    }
                }

                Queue<int> parentIdQueue = new();
                parentIdQueue.Enqueue(options.PID);
                try
                {
                    var process = Process.GetProcessById(options.PID);
                    _Processes[process.Id] = process;
                }
                catch (Exception)
                {
                    // ignore 
                }

                while (parentIdQueue.Count > 0)
                {
                    int front = parentIdQueue.Dequeue();
                    if (!processParentIdDict.TryGetValue(front, out List<ProcessInfo>? processInfos))
                        continue;
                    foreach (ProcessInfo processInfo in processInfos)
                    {
                        try
                        {
                            var childProcess = Process.GetProcessById(processInfo.ProcessId);
                            Console.WriteLine($"    Child Process ID : {childProcess.Id}");
                            Console.WriteLine($"    Child Process Name : {childProcess.ProcessName}");
                            parentIdQueue.Enqueue(processInfo.ProcessId);
                            _Processes[childProcess.Id] = childProcess;
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                foreach (KeyValuePair<int, Process> process in _Processes)
                {
                    List<Process> childProcesses = ProcessHelper.GetChildProcess(process.Value);
                    foreach (Process childProcess in childProcesses)
                    {
                        Console.WriteLine($"    Child Process ID : {childProcess.Id}");
                        Console.WriteLine($"    Child Process Name : {childProcess.ProcessName}");
                        _Processes[childProcess.Id] = childProcess;
                    }
                }
            }

            // if PID is not provided or not found
            // check if ModuleFile is provided
            // if provided, find the process by module file or wait for the process to start
            if (_Processes.IsEmpty && !string.IsNullOrWhiteSpace(options.ModuleFile))
            {
                _WaitingAttach = true;
                Task.Run(() =>
                {
                    Process[] processes = Process.GetProcesses();
                    foreach (Process p in processes)
                    {
                        try
                        {
                            if (!string.Equals(p.MainModule?.FileName, options.ModuleFile,
                                    StringComparison.OrdinalIgnoreCase))
                                continue;
                            _Processes[p.Id] = p;
                            StartMonitorProcessExit(options.WaitingTime);
                            _WaitingAttach = false;
                            break;
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                });
            }

            if (_Processes.IsEmpty && options.PID != 0)
            {
                Console.Error.WriteLine("Can't find process.");
                Environment.Exit(1);
            }

            if (_Session is { IsActive: true })
                _Session.Stop();

            StartMonitorProcessExit(options.WaitingTime);

            Console.WriteLine("Start monitoring...");

            _Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);

            KernelTraceEventParser.Keywords tracingKeywords = KernelTraceEventParser.Keywords.FileIOInit |
                                                              KernelTraceEventParser.Keywords.FileIO |
                                                              KernelTraceEventParser.Keywords.Registry;
            if (options.DisableFileIO)
            {
                tracingKeywords &= ~KernelTraceEventParser.Keywords.FileIO;
                tracingKeywords &= ~KernelTraceEventParser.Keywords.FileIOInit;
            }

            if (options.DisableRegistry)
                tracingKeywords &= ~KernelTraceEventParser.Keywords.Registry;

            // enable kernel provider
            _Session.EnableKernelProvider(tracingKeywords);

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
                        Console.WriteLine(
                            $"[ProcessStart] Process: {data.ProcessName}, Process Id: {data.ProcessID}, Parent Process Id: {data.ParentID}");
                        if (p.MainModule?.FileName != waitImageName) return;
                        _Processes[data.ProcessID] = p;
                        StartMonitorProcessExit(options.WaitingTime);
                        _WaitingAttach = false;
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
                else
                {
                    if (!_Processes.ContainsKey(data.ParentID))
                        return;
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

            _Session.Source.Process();
        }
    }
}