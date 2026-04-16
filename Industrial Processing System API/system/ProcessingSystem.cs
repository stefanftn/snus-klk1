using Industrial_Processing_System_API.config;
using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system;

public class ProcessingSystem
{
    // for priority handling
    private readonly PriorityQueue<Job, int> _queue = new();
    
    // for idempotent handling
    private readonly HashSet<Guid> _seenIds = new();
    
    // for thread-safety on queue
    private readonly object _lock = new();
    
    // for worker threads 
    private readonly SemaphoreSlim _signal = new(0);
    
    // for exiting from system
    private readonly CancellationTokenSource _cts = new();
    
    private readonly int _maxQueueSize;
    private readonly List<Task> _workers = new();

    public event Action<Job, int>? JobCompleted;
    public event Action<Job>? JobFailed;

    public ProcessingSystem(SystemConfig config)
    {
        _maxQueueSize = config.MaxQueueSize;

        for (int i = 0; i < config.WorkerCount; i++)
            _workers.Add(Task.Run(() => WorkerLoop(_cts.Token)));

        foreach (var job in config.Jobs)
            Submit(job);
    }

    public JobHandle? Submit(Job job)
    {
        lock (_lock)
        {
            if (_seenIds.Contains(job.Id))
                return null;

            if (_queue.Count >= _maxQueueSize)
                return null;

            _seenIds.Add(job.Id);
            _queue.Enqueue(job, job.Priority);
        }

        _signal.Release();

        return new JobHandle { Id = job.Id, Result = CreateResultTask(job) };
    }

    private Task<int> CreateResultTask(Job job)
    {
        // placeholder
        return Task.FromResult(0);
    }

    private async Task WorkerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token);

                Job? job = null;
                lock (_lock)
                {
                    if (_queue.Count > 0)
                        job = _queue.Dequeue();
                }

                if (job != null)
                    await ProcessJob(job);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Task ProcessJob(Job job)
    {
        // placeholder
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts.Cancel();
    }
}