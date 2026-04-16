namespace Industrial_Processing_System_API.models;

public class JobHandle
{
    public Guid Id { get; set; }
    public Task<int> Result { get; set; } = Task.FromResult(0);
}