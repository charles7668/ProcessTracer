using System.Collections.Concurrent;

namespace ProcessTracer
{
    public struct TaskMessage
    {
        public string TaskName;
        public string LogMessage;
    }

    public class TaskManager
    {
        private readonly ConcurrentQueue<TaskMessage> _taskQueue = new();
        private CancellationTokenSource _cancellationTokenSource = new();

        private Task? _executorTask;

        private bool _lockEnqueue = false;

        public bool StartTaskExecutor(int workerCount, Func<TaskMessage, CancellationToken, Task> task)
        {
            if (_executorTask is { IsCanceled: true } or { IsCompleted: true })
            {
                return false;
            }

            var tasks = new List<Task>(workerCount);
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancellationTokenSource.Token;
            for (int i = 0; i < workerCount; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    return Task.Run(async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            if (_taskQueue.TryDequeue(out TaskMessage taskMessage))
                            {
                                try
                                {
                                    await task(taskMessage, cancellationToken);
                                }
                                catch
                                {
                                    // if the task fails, try it again
                                    EnqueueTask(taskMessage.TaskName, taskMessage.LogMessage);
                                }
                            }

                            await Task.Delay(1, cancellationToken);
                        }
                    }, cancellationToken);
                }, TaskCreationOptions.LongRunning));
            }

            _lockEnqueue = false;
            _executorTask = Task.WhenAll(tasks);
            return true;
        }

        public async Task StopTaskExecutor(bool waitComplete = false)
        {
            _lockEnqueue = true;
            if (waitComplete)
            {
                while (_taskQueue.Count > 0)
                {
                    await Task.Delay(100);
                }
            }

            await _cancellationTokenSource.CancelAsync();
            try
            {
                _executorTask?.Wait();
            }
            catch (AggregateException ex)
            {
                ex.Handle(x => x is TaskCanceledException);
            }
        }

        public void EnqueueTask(string taskName, string logMessage)
        {
            _taskQueue.Enqueue(new TaskMessage
            {
                TaskName = taskName,
                LogMessage = logMessage
            });
        }
    }
}