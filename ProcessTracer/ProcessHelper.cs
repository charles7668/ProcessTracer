using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Wdk.System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace ProcessTracer
{
    internal static class ProcessHelper
    {
        private static int GetParentProcessId (int processId)
        {
            int parentId = 0;
            var handle = new HANDLE(IntPtr.Zero);
            try
            {
                handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION, false, (uint)processId);
                if (handle != IntPtr.Zero)
                {
                    var pbi = new PROCESS_BASIC_INFORMATION();
                    uint returnLength = 0;
                    unsafe
                    {
                        int status =
                            Windows.Wdk.PInvoke.NtQueryInformationProcess(handle,
                                PROCESSINFOCLASS.ProcessBasicInformation, &pbi,
                                (uint)Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), &returnLength);
                        if (status == 0) // 成功
                        {
                            parentId = (int)pbi.InheritedFromUniqueProcessId.ToUInt32();
                        }
                    }
                }
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    PInvoke.CloseHandle(handle);
                }
            }

            return parentId;
        }

        public static List<Process> GetChildProcess (Process parentProcess)
        {
            var childProcesses = new List<Process>();
            Process[] allProcesses = Process.GetProcesses();

            foreach (Process process in allProcesses)
            {
                int ppid = GetParentProcessId(process.Id);
                if (ppid == parentProcess.Id)
                {
                    childProcesses.Add(process);
                    childProcesses.AddRange(GetChildProcess(process));
                }
            }

            return childProcesses;
        }
    }
}