namespace PingWatch.Worker;

public sealed class WorkerOptions
{
    public const string Section = "Worker";

    /// <summary>How many checks run at once. Also the size of the queue.</summary>
    public int Concurrency { get; set; } = 100;
}