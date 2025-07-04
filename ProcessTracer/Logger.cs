namespace ProcessTracer
{
    public class Logger
    {
        public Logger(RunOptions options)
        {
            _output = options.OutputFile;
            _errorFile = options.OutputErrorFilePath;
            if (string.IsNullOrEmpty(_output))
                LogDelegate = LogToConsole;
            else
            {
                LogDelegate = LogToFile;
                if (!File.Exists(_output))
                {
                    File.Create(_output);
                }
            }

            if (string.IsNullOrEmpty(_errorFile))
                ErrorLogDelegate = ErrorToConsole;
            else
            {
                ErrorLogDelegate = ErrorToFile;
                if (!File.Exists(_errorFile))
                {
                    File.Create(_errorFile);
                }
            }
        }

        private readonly string _output;
        private readonly string _errorFile;

        private Func<string, Task> LogDelegate { get; set; }
        private Func<string, Task> ErrorLogDelegate { get; set; }

        private Task LogToConsole(string message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private Task ErrorToConsole(string message)
        {
            Console.Error.WriteLine(message);
            return Task.CompletedTask;
        }


        private async Task LogToFile(string message)
        {
            await using Stream fs = new FileStream(_output, FileMode.Append);
            await using var sw = new StreamWriter(fs);
            await sw.WriteLineAsync(message);
        }

        private async Task ErrorToFile(string message)
        {
            await using Stream fs = new FileStream(_errorFile, FileMode.Append);
            await using var sw = new StreamWriter(fs);
            await sw.WriteLineAsync(message);
        }

        public async Task LogAsync(string message)
        {
            await LogDelegate(message);
        }

        public void Log(string message)
        {
            LogDelegate(message).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task LogErrorAsync(string message)
        {
            await ErrorLogDelegate(message);
        }

        public void LogError(string message)
        {
            ErrorLogDelegate(message).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}