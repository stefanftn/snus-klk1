namespace Industrial_Processing_System_API.system;

public class IOExecutor
{
    public static int ExecuteIO(int delayMs)
    {
        Thread.Sleep(delayMs);
        return Random.Shared.Next(0, 101);
    }
}