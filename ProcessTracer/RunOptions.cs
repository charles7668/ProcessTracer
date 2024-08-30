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

        [Option("hide", Required = false, HelpText = "Hide console window")]
        public bool HideConsole { get; set; } = false;
    }
}