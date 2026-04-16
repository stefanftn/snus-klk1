using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.executors;

public class IOJobExecutor : IJobExecutor
{
    public JobType JobType => JobType.IO;

    public Task<int> ExecuteAsync(string payload)
    {
        return Task.Run(() =>
        {
            var delay = int.Parse(payload.Split(':')[1].Replace("_", ""));
            return ExecuteIO(delay);
        });
    }
    
    public static int ExecuteIO(int delayMs)
    {
        Thread.Sleep(delayMs);
        return Random.Shared.Next(0, 101);
    }
}