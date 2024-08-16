using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;

namespace ProcessTracer
{
    internal static class ProcessMonitor
    {
        private static TraceEventSession? _Session;

        private static void StartMonitorProcessExit(Process process)
        {
            Task.Run(async () =>
            {
                Console.WriteLine("test1");
                await process.WaitForExitAsync();
                Console.WriteLine("test");
                _Session?.Stop();
            });
        }

        public static void Start(RunOptions options)
        {
            var process = Process.GetProcessById(options.PID);

            Console.WriteLine("Process Info : ");
            Console.WriteLine($"  PID : {process.Id}");
            Console.WriteLine($"  Name : {process.ProcessName}");
            Console.WriteLine("Start monitoring...");
            if (_Session != null && _Session.IsActive)
                _Session.Stop();

            _Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);

            // enable kernel provider with file IO init and file IO keywords
            _Session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit |
                                          KernelTraceEventParser.Keywords.FileIO);

            if (options.UseFileIOWrite)
            {
                _Session.Source.Kernel.FileIOWrite += delegate (FileIOReadWriteTraceData data)
                {
                    if (data.ProcessID == options.PID)
                        Console.WriteLine(
                            $"[FileIOWrite] Process: {data.ProcessName}, File: {data.FileName}");
                };
            }

            if (options.UseFileIOFileCreate)
            {
                _Session.Source.Kernel.FileIOFileCreate += delegate (FileIONameTraceData data)
                {
                    if (data.ProcessID == options.PID)
                        Console.WriteLine($"[FileIOFileCreate] Process: {data.ProcessName}, File: {data.FileName}");
                };
            }

            StartMonitorProcessExit(process);

            _Session.Source.Process();
        }
    }
}