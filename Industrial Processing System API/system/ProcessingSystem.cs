using Industrial_Processing_System_API.config;
using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system;

public class ProcessingSystem
{
    // for priority handling
    private readonly PriorityQueue<Job, int> _queue = new();
    
    // because we cannot traverse priority queue properly
    private readonly List<Job> _queueSnapshot = new();
    
    // for idempotent handling
    private readonly HashSet<Guid> _seenIds = new();
    
    // for reports
    private readonly List<(Job job, int result, bool failed, TimeSpan duration)> _completedJobs = new();
    
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
    
    private readonly EventLogger _logger;
    private readonly ReportGenerator _reportGenerator;

    public ProcessingSystem(SystemConfig config, EventLogger logger, ReportGenerator reportGenerator)
    {
        _maxQueueSize = config.MaxQueueSize;
        _logger = logger;
        _reportGenerator = reportGenerator;
        _ = StartReportTimerAsync(_cts.Token);
        
        JobCompleted += async (job, result) => await _logger.LogCompletedAsync(job, result);
        JobFailed += async (job) => await _logger.LogFailedAsync(job);

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
            _queueSnapshot.Add(job);
            _pendingJobs[job.Id] = tcs;
        }

        _signal.Release();

        return new JobHandle { Id = job.Id, Result = tcs.Task };
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
                    {
                        job = _queue.Dequeue();
                        _queueSnapshot.Remove(job);
                    }
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
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
                    stopwatch.Stop();
                    tcs.SetResult(result);
                    JobCompleted?.Invoke(job, result);
                    
                    lock (_lock)
                    {
                        _pendingJobs.Remove(job.Id);
                        _completedJobs.Add((job, result, false, stopwatch.Elapsed));
                    }
                    return;
                }

                // Timeout — fail
                JobFailed?.Invoke(job);

                if (attempt == Constants.MAX_ATTEMPTS)
                {
                    tcs.SetCanceled();
                    stopwatch.Stop();
                    await LogAbortAsync(job);
                    
                    lock (_lock)
                    {
                        _pendingJobs.Remove(job.Id);
                        _completedJobs.Add((job, -1, true, stopwatch.Elapsed));
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                JobFailed?.Invoke(job);

                if (attempt == Constants.MAX_ATTEMPTS)
                {
                    tcs.SetException(ex);
                    stopwatch.Stop();
                    await LogAbortAsync(job);
                    
                    lock (_lock)
                    {
                        _pendingJobs.Remove(job.Id);
                        _completedJobs.Add((job, -1, true, stopwatch.Elapsed));
                    }
                    return;
                }
            }
        }
    }

    private Task LogAbortAsync(Job job)
    {
        return _logger.LogAbortAsync(job);
    }

    public void Stop()
    {
        _cts.Cancel();
    }
    
    public IEnumerable<Job> GetTopJobs(int n)
    {
        lock (_lock)
        {
            return _queueSnapshot
                .OrderBy(j => j.Priority)
                .Take(n)
                .ToList();
        }
    }

    public Job? GetJob(Guid id)
    {
        lock (_lock)
        {
            return _queueSnapshot.FirstOrDefault(j => j.Id == id);
        }
    }
    
    private async Task StartReportTimerAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Constants.NEW_REPORT_TIMER_MINS));
        while (await timer.WaitForNextTickAsync(token))
        {
            List<(Job, int, bool, TimeSpan)> snapshot;
            lock (_lock)
            {
                snapshot = _completedJobs.ToList();
            }
            _reportGenerator.GenerateReport(snapshot);
        }
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