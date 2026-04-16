using Industrial_Processing_System_API.config;
using Industrial_Processing_System_API.models;
using Industrial_Processing_System_API.system;

namespace Industrial_Processing_System_API;

public static class Program
{
    static async Task Main(string[] args)
    {
        var config = ConfigLoader.Load("config" + Path.DirectorySeparatorChar + "SystemConfig.xml");
        var logger = new EventLogger("events.log");
        var generator = new ReportGenerator();
        var system = new ProcessingSystem(config, logger, generator);

        Console.WriteLine($"System running sa {config.WorkerCount} workers and max queue size {config.MaxQueueSize}");
        
        var producers = Enumerable.Range(0, config.WorkerCount)
            .Select(i => Task.Run(() => ProducerLoop(system, i)))
            .ToList();

        Console.WriteLine("ENTER za stop...");
        Console.ReadLine();

        system.Stop();
        await Task.WhenAll(producers);

        Console.WriteLine("System stopped.");
    }

    static void ProducerLoop(ProcessingSystem system, int producerId)
    {
        var rng = new Random();
        var jobTypes = Enum.GetValues<JobType>();

        while (true)
        {
            try
            {
                var type = jobTypes[rng.Next(jobTypes.Length)];

                var job = new Job
                {
                    Id = Guid.NewGuid(),
                    Type = type,
                    Payload = GeneratePayload(type, rng),
                    Priority = rng.Next(1, 6)
                };

                var handle = system.Submit(job);

                if (handle == null)
                {
                    Console.WriteLine($"[Producer {producerId}] Job {job.Id} rejected.");
                }
                else
                {
                    Console.WriteLine($"[Producer {producerId}] Job {job.Id} ({job.Type}, P{job.Priority}) added.");

                    _ = WaitForResult(handle, producerId);
                }

                Thread.Sleep(rng.Next(100, 500));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Producer {producerId}] Error: {ex.Message}");
            }
        }
    }

    static async Task WaitForResult(JobHandle handle, int producerId)
    {
        try
        {
            int result = await handle.Result;
            Console.WriteLine($"[Producer {producerId}] Job {handle.Id} finished, result: {result}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[Producer {producerId}] Job {handle.Id} ABORTED.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Producer {producerId}] Job {handle.Id} error: {ex.Message}");
        }
    }

    static string GeneratePayload(JobType type, Random rng)
    {
        return type switch
        {
            JobType.Prime => $"numbers:{rng.Next(1000, 50000)},threads:{rng.Next(1, 9)}",
            JobType.IO => $"delay:{rng.Next(100, 4000)}",
            _ => throw new InvalidOperationException()
        };
    }
}