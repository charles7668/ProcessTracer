using System.IO.Pipes;

namespace ProcessTracer
{
    public static class TaskExecutor
    {
        private static async Task RunPipeServerInstanceAsync(string pipeName, TaskManager taskManager,
            Func<string, Task<bool>> receiveLineCallback, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var pipeServer = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough
                    );

                    await pipeServer.WaitForConnectionAsync(cancellationToken);

                    using var reader = new StreamReader(pipeServer);
                    while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    {
                        taskManager.EnqueueTask("Log", "Received: " + line);
                        if (!await receiveLineCallback(line))
                        {
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(100, CancellationToken.None);
                }
            }
        }

        public static async Task StartNamedPipeReceiveTaskAsync(string pipeName, Logger logger,
            Func<string, Task<bool>> receiveLineCallback, CancellationToken cancellationToken)
        {
            int threadCount = Environment.ProcessorCount;
            var tasks = new List<Task>(threadCount);
            TaskManager taskManager = new();
            taskManager.StartTaskExecutor(threadCount, (msg, token) =>
            {
                return msg.TaskName switch
                {
                    "Log" => logger.LogAsync(msg.LogMessage, token),
                    "Error" => logger.LogErrorAsync(msg.LogMessage, token),
                    _ => Task.CompletedTask
                };
            });
            for (int i = 0; i < threadCount; i++)
            {
                tasks.Add(Task.Factory
                    .StartNew(
                        () => RunPipeServerInstanceAsync(pipeName, taskManager, receiveLineCallback, cancellationToken),
                        TaskCreationOptions.LongRunning).Unwrap());
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                await taskManager.StopTaskExecutor(true);
            }
        }
    }
}