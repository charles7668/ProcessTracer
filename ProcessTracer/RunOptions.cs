using CommandLine;

namespace ProcessTracer
{
    internal class RunOptions
    {
        [Option('p', "pid", Required = true, HelpText = "Your process id for tracing")]
        public int PID { get; set; } = 0;

        [Option("use-file-io-write", Required = false, HelpText = "Enable file IO write tracing", Default = true)]
        public bool UseFileIOWrite { get; set; }

        [Option("use-file-io-file-create", Required = false, HelpText = "Enable file IO file create tracing",
            Default = true)]
        public bool UseFileIOFileCreate { get; set; }
    }
}