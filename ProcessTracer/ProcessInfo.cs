using JetBrains.Annotations;

namespace ProcessTracer
{
    public class ProcessInfo(string processName, int processId, int parentProcessId, string? executablePath)
    {
        [UsedImplicitly]
        public string ProcessName { get; set; } = processName;

        [UsedImplicitly]
        public int ProcessId { get; set; } = processId;

        [UsedImplicitly]
        public int ParentProcessId { get; set; } = parentProcessId;

        [UsedImplicitly]
        public string? ExecutablePath { get; set; } = executablePath;
    }
}