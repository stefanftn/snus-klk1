using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.loggers;

public interface IEventLogger
{
    Task LogCompletedAsync(Job job, int result);
    Task LogFailedAsync(Job job);
    Task LogAbortAsync(Job job);
}