using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.config;

public class SystemConfig
{
    public int WorkerCount { get; set; }
    public int MaxQueueSize { get; set; }
    public List<Job> Jobs { get; set; } = new();
}