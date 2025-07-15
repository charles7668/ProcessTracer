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

        private Func<string, CancellationToken, Task> LogDelegate { get; set; }
        private Func<string, CancellationToken, Task> ErrorLogDelegate { get; set; }

        private readonly SemaphoreSlim _writeSlim = new(1, 1);

        private Task LogToConsole(string message, CancellationToken cancellationToken)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private Task ErrorToConsole(string message, CancellationToken cancellationToken)
        {
            Console.Error.WriteLine(message);
            return Task.CompletedTask;
        }


        private async Task LogToFile(string message, CancellationToken cancellationToken)
        {
            await _writeSlim.WaitAsync(CancellationToken.None);
            try
            {
                await using var writer = new StreamWriter(_output, append: true);
                await writer.WriteLineAsync(message);
            }
            finally
            {
                _writeSlim.Release();
            }
        }

        private async Task ErrorToFile(string message, CancellationToken cancellationToken)
        {
            await _writeSlim.WaitAsync(CancellationToken.None);
            try
            {
                await using var writer = new StreamWriter(_errorFile, append: true);
                await writer.WriteLineAsync(message);
            }
            finally
            {
                _writeSlim.Release();
            }
        }

        public async Task LogAsync(string message, CancellationToken cancellationToken)
        {
            await LogDelegate(message, cancellationToken);
        }

        public void Log(string message)
        {
            LogDelegate(message, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task LogErrorAsync(string message, CancellationToken cancellationToken)
        {
            await ErrorLogDelegate(message, cancellationToken);
        }

        public void LogError(string message)
        {
            ErrorLogDelegate(message, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}