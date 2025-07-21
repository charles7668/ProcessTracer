using System.IO.Pipes;

namespace ProcessTracer
{
    public class Logger
    {
        public Logger(RunOptions options)
        {
            _output = options.OutputFile;
            _errorFile = options.OutputErrorFilePath;
            if (options.Parent != 0)
            {
                LogDelegate = CreateParentLogger(options.Parent);
                ErrorLogDelegate = CreateParentLogger(options.Parent);
            }
            else
            {
                if (string.IsNullOrEmpty(_output))
                    LogDelegate = LogToConsole;
                else
                {
                    LogDelegate = LogToFile;
                    if (!File.Exists(_output))
                    {
                        using (File.Create(_errorFile)) ;
                    }
                }

                if (string.IsNullOrEmpty(_errorFile))
                    ErrorLogDelegate = ErrorToConsole;
                else
                {
                    ErrorLogDelegate = ErrorToFile;
                    if (!File.Exists(_errorFile))
                    {
                        using (File.Create(_errorFile)) ;
                    }
                }
            }
        }

        private readonly string _output;
        private readonly string _errorFile;

        private Func<string, CancellationToken, Task> LogDelegate { get; set; }
        private Func<string, CancellationToken, Task> ErrorLogDelegate { get; set; }

        private readonly SemaphoreSlim _writeOutputSemaphore = new(1, 1);
        private readonly SemaphoreSlim _writeErrorSemaphore = new(1, 1);

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

        private Func<string, CancellationToken, Task> CreateParentLogger(int parentPid)
        {
            return async (message, cancellationToken) =>
            {
                message = message.StartsWith("Received: ") ? message.Substring("Received: ".Length) : message;                string pipeName = "ProcessTracerPipe:" + parentPid;
                await using var pipeClient =
                    new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                        PipeOptions.Asynchronous);

                await pipeClient.ConnectAsync(1000, cancellationToken);
                if (!pipeClient.IsConnected)
                    throw new IOException($"Can't connect to {pipeName}");

                await using var writer = new StreamWriter(pipeClient);
                writer.AutoFlush = true;
                await writer.WriteLineAsync(message);
            };
        }

        private async Task LogToFile(string message, CancellationToken cancellationToken)
        {
            await _writeOutputSemaphore.WaitAsync(CancellationToken.None);
            try
            {
                await using var writer = new StreamWriter(_output, append: true);
                await writer.WriteLineAsync(message);
            }
            finally
            {
                _writeOutputSemaphore.Release();
            }
        }

        private async Task ErrorToFile(string message, CancellationToken cancellationToken)
        {
            await _writeErrorSemaphore.WaitAsync(CancellationToken.None);
            try
            {
                await using var writer = new StreamWriter(_errorFile, append: true);
                await writer.WriteLineAsync(message);
            }
            finally
            {
                _writeErrorSemaphore.Release();
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