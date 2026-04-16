namespace Industrial_Processing_System_API.config;

public class PayloadParser
{
    public static (int numbers, int threads) ParsePrimePayload(string payload)
    {
        // Format: "numbers:10_000,threads:3"
        var parts = payload.Split(',');
        int numbers = int.Parse(parts[0].Split(':')[1].Replace("_", ""));
        int threads = int.Parse(parts[1].Split(':')[1]);
        threads = Math.Clamp(threads, 1, 8);
        return (numbers, threads);
    }

    public static int ParseIOPayload(string payload)
    {
        // Format: "delay:1_000"
        return int.Parse(payload.Split(':')[1].Replace("_", ""));
    }
}