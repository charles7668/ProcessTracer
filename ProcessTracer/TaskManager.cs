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
                tasks.Add(Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (_taskQueue.TryDequeue(out TaskMessage taskMessage))
                        {
                            await task(taskMessage, cancellationToken);
                        }

                        await Task.Delay(1, cancellationToken);
                    }
                }, cancellationToken));
            }

            _executorTask = Task.WhenAll(tasks);
            return true;
        }

        public void StopTaskExecutor()
        {
            _cancellationTokenSource.Cancel();
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