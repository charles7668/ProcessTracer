﻿using CommandLine;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace ProcessTracer
{
    internal static class Program
    {
        private static unsafe bool CanElevate()
        {
            HANDLE hToken = HANDLE.Null;
            TOKEN_ELEVATION_TYPE tokenType = TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
            if (!PInvoke.OpenProcessToken((HANDLE)Process.GetCurrentProcess().Handle,
                    TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS,
                    &hToken)) return false;
            SafeHandleWrapper safeHandle = new(hToken.Value);
            PInvoke.GetTokenInformation(safeHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, &tokenType,
                sizeof(TOKEN_ELEVATION_TYPE), out uint _);
            PInvoke.CloseHandle(hToken);

            return tokenType == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
        }

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            foreach (Error error in errors)
                Console.Error.WriteLine(error.ToString());

            Environment.Exit(1);
        }

        private static void RunElevate(string[] args)
        {
            ProcessStartInfo elevationInfo = new()
            {
                FileName = Process.GetCurrentProcess().ProcessName,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args),
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            try
            {
                Process.Start(elevationInfo);
                Environment.Exit(0);
            }
            catch
            {
                Console.Error.WriteLine("process can't start with admin rights");
                Environment.Exit(1);
            }
        }

        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // this program need start with admin rights
            if (CanElevate())
            {
                RunElevate(args);
            }

            Parser.Default.ParseArguments<RunOptions>(args)
                .WithParsed(StartWithRunOptions)
                .WithNotParsed(HandleParseError);
        }

        private static void StartWithRunOptions(RunOptions options)
        {
            ProcessMonitor.Start(options);
            Environment.Exit(0);
        }
    }
}