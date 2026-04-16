namespace Industrial_Processing_System_API.system;

public class PrimeExecutor
{
    public static int ExecutePrime(int limit, int threadCount)
    {
        var ranges = Enumerable.Range(0, threadCount)
            .Select(i =>
            {
                int start = i * (limit / threadCount) + 1;
                int end = (i == threadCount - 1) ? limit : (i + 1) * (limit / threadCount);
                return (start, end);
            });

        int totalPrimes = 0;

        Parallel.ForEach(ranges,
            new ParallelOptions { MaxDegreeOfParallelism = threadCount },
            range =>
            {
                int localCount = 0;
                for (int n = range.start; n <= range.end; n++)
                {
                    if (IsPrime(n)) localCount++;
                }
                Interlocked.Add(ref totalPrimes, localCount);
            });

        return totalPrimes;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;
        for (int i = 3; i * i <= n; i += 2)
            if (n % i == 0) return false;
        return true;
    }
}