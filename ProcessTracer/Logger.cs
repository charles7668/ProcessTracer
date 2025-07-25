using System.IO.Pipes;

namespace ProcessTracer
{
    public class Logger : IAsyncDisposable, IDisposable
    {
        public Logger(RunOptions options)
        {
            string output = options.OutputFile;
            string errorFile = options.OutputErrorFilePath;
            if (options.Parent != 0)
            {
                LogDelegate = CreateParentLogger(options.Parent);
                ErrorLogDelegate = CreateParentLogger(options.Parent);
            }
            else
            {
                if (string.IsNullOrEmpty(output))
                    LogDelegate = LogToConsole;
                else
                {
                    _outStreamWriter = new StreamWriter(output, true);
                    LogDelegate = LogToFile;
                }

                if (string.IsNullOrEmpty(errorFile))
                    ErrorLogDelegate = ErrorToConsole;
                else
                {
                    _errorStreamWriter = new StreamWriter(errorFile, true);
                    ErrorLogDelegate = ErrorToFile;
                }
            }
        }

        private readonly StreamWriter? _errorStreamWriter;

        private readonly StreamWriter? _outStreamWriter;
        private readonly SemaphoreSlim _writeErrorSemaphore = new(1, 1);

        private readonly SemaphoreSlim _writeOutputSemaphore = new(1, 1);

        private Func<string, CancellationToken, Task> LogDelegate { get; }
        private Func<string, CancellationToken, Task> ErrorLogDelegate { get; }

        public async ValueTask DisposeAsync()
        {
            if (_outStreamWriter != null) await _outStreamWriter.DisposeAsync();
            if (_errorStreamWriter != null) await _errorStreamWriter.DisposeAsync();
            await CastAndDispose(_writeOutputSemaphore);
            await CastAndDispose(_writeErrorSemaphore);

            return;

            static async ValueTask CastAndDispose(IDisposable resource)
            {
                if (resource is IAsyncDisposable resourceAsyncDisposable)
                    await resourceAsyncDisposable.DisposeAsync();
                else
                    resource.Dispose();
            }
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

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
                message = message.StartsWith("Received: ") ? message.Substring("Received: ".Length) : message;
                string pipeName = "ProcessTracerPipe:" + parentPid;
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
                if (_outStreamWriter != null)
                    await _outStreamWriter.WriteLineAsync(message);
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
                if (_errorStreamWriter != null)
                    await _errorStreamWriter.WriteLineAsync(message);
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