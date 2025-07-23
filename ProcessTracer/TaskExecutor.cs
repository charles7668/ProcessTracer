using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ProcessTracer
{
    public static class TaskExecutor
    {
        public static Task StartNamedPipeReceiveTaskAsync(string pipeName, Logger logger,
            CancellationToken cancellationToken, Func<string, Task<bool>> receiveLineCallback)
        {
            int threadCount = Environment.ProcessorCount;
            var tasks = new List<Task>(threadCount);
            TaskManager taskManager = new();
            taskManager.StartTaskExecutor(threadCount, (msg, token) =>
            {
                switch (msg.TaskName)
                {
                    case "Log":
                        return logger.LogAsync(msg.LogMessage, token);
                    case "Error":
                        return logger.LogErrorAsync(msg.LogMessage, token);
                }

                return Task.CompletedTask;
            });
            for (int i = 0; i < threadCount; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    return Task.Run(async () =>
                    {
                        while (true)
                        {
                            await using var pipeServer = new NamedPipeServerStream(
                                pipeName,
                                PipeDirection.In,
                                NamedPipeServerStream.MaxAllowedServerInstances,
                                PipeTransmissionMode.Byte,
                                PipeOptions.Asynchronous | PipeOptions.WriteThrough
                            );
                            await pipeServer.WaitForConnectionAsync(cancellationToken);
                            if (cancellationToken.IsCancellationRequested)
                                return;

                            var reader = new StreamReader(pipeServer);
                            try
                            {
                                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                                {
                                    bool needContinue = await receiveLineCallback(line);
                                    if (!needContinue)
                                        return;
                                    taskManager.EnqueueTask("Log", "Received: " + line);
                                }
                            }
                            catch
                            {
                                // ignore
                            }

                            if (cancellationToken.IsCancellationRequested)
                                return;
                        }
                    }, cancellationToken);
                }, TaskCreationOptions.LongRunning).Unwrap());
            }

            return Task.WhenAll(tasks).ContinueWith(async _ =>
            {
                await taskManager.StopTaskExecutor(true);
            }, CancellationToken.None).Unwrap();
        }
    }
}