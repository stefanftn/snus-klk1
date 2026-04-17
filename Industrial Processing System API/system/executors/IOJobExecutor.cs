using Industrial_Processing_System_API.config;
using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.executors;

public class IOJobExecutor : IJobExecutor
{
    public JobType JobType => JobType.IO;

    public Task<int> ExecuteAsync(string payload)
    {
        return Task.Run(() =>
        {
            var delay = PayloadParser.ParseIOPayload(payload);
            return ExecuteIO(delay);
        });
    }
    
    private static int ExecuteIO(int delayMs)
    {
        Thread.Sleep(delayMs);
        return Random.Shared.Next(0, 101);
    }
}