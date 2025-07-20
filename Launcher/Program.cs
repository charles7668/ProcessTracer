using System.Diagnostics;

namespace Launcher
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            for(int i = 0 ; i < args.Length; i++)
            {
                args[i] = args[i].Replace("\"", "\\\"");
                args[i] = "\"" + args[i] + "\"";
            }
            ProcessStartInfo elevationInfo = new()
            {
                FileName = "ProcessTracer",
                UseShellExecute = false,
                Arguments = string.Join(" ", args),
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            var proc = Process.Start(elevationInfo);
            proc?.WaitForExit();
        }
    }
}