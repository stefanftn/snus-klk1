using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.executors;

public interface IJobExecutor
{
    JobType JobType { get; }
    Task<int> ExecuteAsync(string payload);
}