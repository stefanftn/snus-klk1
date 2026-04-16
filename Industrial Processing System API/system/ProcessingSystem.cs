using Industrial_Processing_System_API.config;
using Industrial_Processing_System_API.models;
using Industrial_Processing_System_API.system.executors;
using Industrial_Processing_System_API.system.loggers;
using Industrial_Processing_System_API.system.report_generators;

public class ProcessingSystem
{
    private readonly PriorityQueue<Job, int> _queue = new();
    private readonly List<Job> _queueSnapshot = new();
    private readonly HashSet<Guid> _seenIds = new();
    private readonly List<(Job job, int result, bool failed, TimeSpan duration)> _completedJobs = new();
    private readonly Dictionary<Guid, TaskCompletionSource<int>> _pendingJobs = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxQueueSize;
    private readonly List<Task> _workers = new();

    public event Action<Job, int>? JobCompleted;
    public event Action<Job>? JobFailed;

    private readonly Dictionary<JobType, IJobExecutor> _executors;
    private readonly IEventLogger _logger;
    private readonly IReportGenerator _reportGenerator;

    public ProcessingSystem(
        SystemConfig config,
        IEventLogger logger,
        IReportGenerator reportGenerator,
        IEnumerable<IJobExecutor> executors
        )
    {
        _maxQueueSize = config.MaxQueueSize;
        _logger = logger;
        _reportGenerator = reportGenerator;
        _executors = executors.ToDictionary(e => e.JobType);

        _ = StartReportTimerAsync(_cts.Token);

        JobCompleted += async (job, result) => await _logger.LogCompletedAsync(job, result);
        JobFailed += async (job) => await _logger.LogFailedAsync(job);

        for (int i = 0; i < config.WorkerCount; i++)
            _workers.Add(Task.Run(() => WorkerLoop(_cts.Token)));

        foreach (var job in config.Jobs)
            Submit(job);
    }

    public JobHandle? Submit(Job job)
    {
        var tcs = new TaskCompletionSource<int>();

        lock (_lock)
        {
            if (_seenIds.Contains(job.Id)) return null;
            if (_queue.Count >= _maxQueueSize) return null;

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
        lock (_lock) { _pendingJobs.TryGetValue(job.Id, out tcs); }
        if (tcs == null) return;

        if (!_executors.TryGetValue(job.Type, out var executor))
        {
            tcs.SetException(new InvalidOperationException($"No executor for job type: {job.Type}"));
            return;
        }

        for (int attempt = 1; attempt <= Constants.MAX_ATTEMPTS; attempt++)
        {
            try
            {
                var jobTask = executor.ExecuteAsync(job.Payload);
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

                JobFailed?.Invoke(job);

                if (attempt == Constants.MAX_ATTEMPTS)
                {
                    tcs.SetCanceled();
                    stopwatch.Stop();
                    await _logger.LogAbortAsync(job);

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
                    await _logger.LogAbortAsync(job);

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

    public IEnumerable<Job> GetTopJobs(int n)
    {
        lock (_lock)
        {
            return _queueSnapshot.OrderBy(j => j.Priority).Take(n).ToList();
        }
    }

    public Job? GetJob(Guid id)
    {
        lock (_lock)
        {
            return _queueSnapshot.FirstOrDefault(j => j.Id == id);
        }
    }

    public void Stop() => _cts.Cancel();

    private async Task StartReportTimerAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Constants.NEW_REPORT_TIMER_MINS));
        while (await timer.WaitForNextTickAsync(token))
        {
            List<(Job, int, bool, TimeSpan)> snapshot;
            lock (_lock) { snapshot = _completedJobs.ToList(); }
            _reportGenerator.GenerateReport(snapshot);
        }
    }
}