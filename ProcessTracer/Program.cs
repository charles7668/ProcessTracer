using CommandLine;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace ProcessTracer
{
    internal static class Program
    {
        private static readonly List<string> _OriginalArgs = [];

        private static Logger _Logger = null!;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        public static unsafe bool CanElevate()
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
                var proc = Process.Start(elevationInfo);
                proc?.WaitForExit();
                // Environment.Exit(0);
            }
            catch
            {
                Console.Error.WriteLine("process can't start with admin rights");
                Environment.Exit(1);
            }
        }

        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            foreach (string s in args)
            {
                Console.WriteLine(s);
                string temp = s.Replace("\"", "\\\"");
                _OriginalArgs.Add("\"" + temp + "\"");
            }

            Parser.Default.ParseArguments<RunOptions>(args)
                .WithParsed(StartMonitor)
                .WithNotParsed(HandleParseError);
        }

        private static void StartMonitor(RunOptions options)
        {
            _Logger = new Logger(options);
            ProcessMonitor monitor = new(options, _Logger);
            bool needRestart = monitor.Start().ConfigureAwait(false).GetAwaiter().GetResult();
            if (needRestart && CanElevate())
            {
                RunElevate(_OriginalArgs.ToArray());
            }
        }
    }
}