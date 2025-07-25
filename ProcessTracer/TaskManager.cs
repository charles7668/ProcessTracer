using System.Collections.Concurrent;

namespace ProcessTracer
{
    public record TaskMessage
    {
        public string TaskName { get; init; } = string.Empty;
        public string LogMessage { get; init; } = string.Empty;
        public int RetryCount { get; init; }
    }

    public class TaskManager : IAsyncDisposable
    {
        private readonly BlockingCollection<TaskMessage> _taskQueue = new(new ConcurrentQueue<TaskMessage>());
        private CancellationTokenSource _cancellationTokenSource = new();

        private Task? _executorTask;

        public bool StartTaskExecutor(int workerCount, Func<TaskMessage, CancellationToken, Task> task)
        {
            if (_executorTask?.IsCompleted == false)
            {
                return false;
            }

            var tasks = new List<Task>(workerCount);
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancellationTokenSource.Token;
            for (int i = 0; i < workerCount; i++)
            {
                tasks.Add(Task.Factory.StartNew(async () =>
                {
                    foreach (TaskMessage taskMessage in _taskQueue.GetConsumingEnumerable(cancellationToken))
                    {
                        try
                        {
                            await task(taskMessage, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception)
                        {
                            int retryCount = taskMessage.RetryCount;
                            if (retryCount < 3)
                            {
                                EnqueueTask(taskMessage with
                                {
                                    RetryCount = retryCount + 1
                                });
                            }
                        }
                    }
                }, TaskCreationOptions.LongRunning).Unwrap());
            }

            _executorTask = Task.WhenAll(tasks);
            return true;
        }

        public async Task StopTaskExecutor(bool processRemaining = false)
        {
            if (_executorTask is null)
                return;
            _taskQueue.CompleteAdding();

            if (!processRemaining)
            {
                await _cancellationTokenSource.CancelAsync();
            }

            try
            {
                await _executorTask;
            }
            catch (OperationCanceledException)
            {
            }

            _cancellationTokenSource.Dispose();
        }

        private void EnqueueTask(TaskMessage taskMessage)
        {
            if (_taskQueue.IsAddingCompleted)
                return;
            _taskQueue.Add(taskMessage);
        }

        public void EnqueueTask(string taskName, string logMessage)
        {
            EnqueueTask(new TaskMessage
            {
                TaskName = taskName,
                LogMessage = logMessage
            });
        }

        public async ValueTask DisposeAsync()
        {
            await CastAndDispose(_taskQueue);
            await CastAndDispose(_cancellationTokenSource);
            if (_executorTask != null)
                await CastAndDispose(_executorTask);

            return;

            static async ValueTask CastAndDispose(IDisposable resource)
            {
                if (resource is IAsyncDisposable resourceAsyncDisposable)
                    await resourceAsyncDisposable.DisposeAsync();
                else
                    resource.Dispose();
            }
        }
    }
}