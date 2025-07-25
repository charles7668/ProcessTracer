﻿using CommandLine;
using JetBrains.Annotations;

namespace ProcessTracer
{
    public class RunOptions
    {
        [Option('f', "file", Required = false,
            HelpText = "Executable file witch will be monitored")]
        [UsedImplicitly]
        public string Executable { get; set; } = string.Empty;

        [Option('a', "args", Required = false,
            HelpText = "Arguments for your executable")]
        [UsedImplicitly]
        public string Arguments { get; set; } = string.Empty;

        [Option('o', "output", Required = false,
            HelpText = "Output file for tracing results , if not set than use console")]
        [UsedImplicitly]
        public string OutputFile { get; set; } = string.Empty;

        [Option('e', "error", Required = false,
            HelpText = "Error file output path , if not set than use console")]
        [UsedImplicitly]
        public string OutputErrorFilePath { get; set; } = string.Empty;

        [Option("hide", Required = false, HelpText = "Hide console window")]
        [UsedImplicitly]
        public bool HideConsole { get; set; }

        [Option("runas", Required = false, HelpText = "Run application using admin")]
        [UsedImplicitly]
        public bool RunAs { get; set; }

        [Option("parent", Required = false, HelpText = "The parent ProcessTracer process (PID) will receive messages, which will then be forwarded to the parent process.")]
        [UsedImplicitly]
        public int Parent { get; set; }
    }
}