using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system;

public class EventLogger(string logPath = "events.log")
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task LogCompletedAsync(Job job, int result)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [COMPLETED] {job.Id}, {result}";
        await WriteLineAsync(line);
    }

    public async Task LogFailedAsync(Job job)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FAILED] {job.Id}, -";
        await WriteLineAsync(line);
    }

    public async Task LogAbortAsync(Job job)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ABORT] {job.Id}, -";
        await WriteLineAsync(line);
    }

    private async Task WriteLineAsync(string line)
    {
        await _fileLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}