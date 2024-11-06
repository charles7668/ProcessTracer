using CommandLine;
using JetBrains.Annotations;

namespace ProcessTracer
{
    internal class RunOptions
    {
        [Option('p', "pid", Required = false, HelpText = "Your process id for tracing")]
        [UsedImplicitly]
        public int PID { get; set; }

        [Option('f', "file", Required = false,
            HelpText = "Your module file for tracing , if set pid then this setting will be ignored")]
        [UsedImplicitly]
        public string ModuleFile { get; set; } = string.Empty;

        [Option('w', "wait", Required = false,
            HelpText = "Waiting time for attach process when using --file option; if set to 0, the time is infinite")]
        [UsedImplicitly]
        public int WaitingTime { get; set; }

        [Option("hide", Required = false, HelpText = "Hide console window")]
        [UsedImplicitly]
        public bool HideConsole { get; set; }

        [Option("disable-registry", Required = false, HelpText = "Disable registry event")]
        [UsedImplicitly]
        public bool DisableRegistry { get; set; }

        [Option("disable-fileio", Required = false, HelpText = "Disable file io event")]
        [UsedImplicitly]
        public bool DisableFileIO { get; set; }
    }
}