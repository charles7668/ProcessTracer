using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.System.Threading;

namespace ProcessTracer
{
    internal static class DetoursLoader
    {
        [DllImport("DetoursLoader.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool DetourCreateProcessWithDllAWrap(
            [In] string lpApplicationName,
            [In] string? lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            [In] string? lpCurrentDirectory,
            [In] ref STARTUPINFOA lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation,
            [In] uint nDlls,
            [In] string[] lpDllName,
            [In] string pipeHandle
        );

        [DllImport("DetoursLoader.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint GetDetourCreateProcessError();
    }
}