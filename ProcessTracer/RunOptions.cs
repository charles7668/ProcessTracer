using CommandLine;

namespace ProcessTracer
{
    internal class RunOptions
    {
        [Option('p', "pid", Required = true, HelpText = "Your process id for tracing")]
        public int PID { get; set; } = 0;
    }
}