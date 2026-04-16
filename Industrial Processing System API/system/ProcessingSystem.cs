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
    
    private readonly Dictionary<Guid, TaskCompletionSource<int>> _pendingJobs = new();

    public JobHandle? Submit(Job job)
    {
        var tcs = new TaskCompletionSource<int>();

        lock (_lock)
        {
            if (_seenIds.Contains(job.Id))
                return null;

            if (_queue.Count >= _maxQueueSize)
                return null;

            _seenIds.Add(job.Id);
            _queue.Enqueue(job, job.Priority);
            _pendingJobs[job.Id] = tcs;
        }

        _signal.Release();

        return new JobHandle { Id = job.Id, Result = tcs.Task };
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

    private async Task ProcessJob(Job job)
    {

        TaskCompletionSource<int>? tcs;
        lock (_lock)
        {
            _pendingJobs.TryGetValue(job.Id, out tcs);
        }

        if (tcs == null) return;

        for (int attempt = 1; attempt <= Constants.MAX_ATTEMPTS; attempt++)
        {
            try
            {
                var jobTask = job.Type switch
                {
                    JobType.Prime => Task.Run(() =>
                    {
                        var (numbers, threads) = ParsePrimePayload(job.Payload);
                        return PrimeExecutor.ExecutePrime(numbers, threads);
                    }),
                    JobType.IO => Task.Run(() =>
                    {
                        var delay = ParseIOPayload(job.Payload);
                        return IOExecutor.ExecuteIO(delay);
                    }),
                    _ => throw new InvalidOperationException($"Unknown job type: {job.Type}")
                };

                // waiting result with timeout
                var completed = await Task.WhenAny(jobTask, Task.Delay(Constants.TIMEOUT_MS));

                if (completed == jobTask)
                {
                    int result = await jobTask;
                    tcs.SetResult(result);
                    JobCompleted?.Invoke(job, result);
                    return;
                }

                // Timeout — fail
                JobFailed?.Invoke(job);

                if (attempt == Constants.MAX_ATTEMPTS)
                {
                    // ABORT nakon 3 pokušaja
                    tcs.SetCanceled();
                    await LogAbortAsync(job);
                    return;
                }
            }
            catch (Exception ex)
            {
                JobFailed?.Invoke(job);

                if (attempt == Constants.MAX_ATTEMPTS)
                {
                    tcs.SetException(ex);
                    await LogAbortAsync(job);
                    return;
                }
            }
        }
    }

    private static Task LogAbortAsync(Job job)
    {
        // placeholder
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts.Cancel();
    }
    
    private static (int numbers, int threads) ParsePrimePayload(string payload)
    {
        // Format: "numbers:10_000,threads:3"
        var parts = payload.Split(',');
        int numbers = int.Parse(parts[0].Split(':')[1].Replace("_", ""));
        int threads = int.Parse(parts[1].Split(':')[1]);
        threads = Math.Clamp(threads, 1, 8);
        return (numbers, threads);
    }

    private static int ParseIOPayload(string payload)
    {
        // Format: "delay:1_000"
        return int.Parse(payload.Split(':')[1].Replace("_", ""));
    }
}