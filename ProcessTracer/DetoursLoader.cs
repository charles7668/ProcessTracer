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
        [DllImport("DetoursLoader.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern bool DetourCreateProcessWithDllWWrap(
            [In] byte[] lpApplicationName,
            [In] byte[] lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            [In] string? lpCurrentDirectory,
            [In] ref STARTUPINFOW lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation,
            [In] uint nDlls,
            [In] IntPtr lpDllName,
            [In] byte[] pipeHandle
        );

        [DllImport("DetoursLoader.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint GetDetourCreateProcessError();
    }
}