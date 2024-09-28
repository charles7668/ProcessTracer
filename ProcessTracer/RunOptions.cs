using CommandLine;

namespace ProcessTracer
{
    internal class RunOptions
    {
        [Option('p', "pid", Required = false, HelpText = "Your process id for tracing")]
        public int PID { get; set; } = 0;

        [Option('f', "file", Required = false,
            HelpText = "Your module file for tracing , if set pid then this setting will be ignored")]
        public string ModuleFile { get; set; } = string.Empty;

        [Option('w', "wait", Required = false, HelpText = "Waiting time for attach process when using --file option; if set to 0, the time is infinite")]
        public int WaitingTime { get; set; } = 0;

        [Option("hide", Required = false, HelpText = "Hide console window")]
        public bool HideConsole { get; set; } = false;
    }
}