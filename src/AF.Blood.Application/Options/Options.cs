namespace AF.Blood.Application.Options;

public class MatchingOptions
{
    public const string SectionName = "Matching";

    /// <summary>Radius ladder in km; first entry is the initial radius (25).</summary>
    public int[] RadiiKm { get; set; } = [25, 50, 100];

    /// <summary>Delay between waves.</summary>
    public int WaveDelayMinutes { get; set; } = 20;

    /// <summary>Maximum donors notified per wave.</summary>
    public int BatchSizePerWave { get; set; } = 40;

    /// <summary>A wave that reaches this many newly-notified donors stops the ladder.</summary>
    public int MinCandidatesToStop { get; set; } = 10;

    /// <summary>Lease duration for a claimed matching run.</summary>
    public int LeaseMinutes { get; set; } = 5;
}

public class RequestOptions
{
    public const string SectionName = "Requests";

    public int DefaultExpiryHours { get; set; } = 48;
    public int MinExpiryHours { get; set; } = 1;
    public int MaxExpiryHours { get; set; } = 168;
    public int MaxOpenRequestsPerUser { get; set; } = 5;
}

public class DonationOptions
{
    public const string SectionName = "Donations";

    /// <summary>How far back a donation date may lie when reporting.</summary>
    public int MaxReportAgeDays { get; set; } = 30;
}

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public int OutboxBatchSize { get; set; } = 20;
    public int LeaseMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 8;
    public int RetryBaseSeconds { get; set; } = 5;
    public int RetryMaxSeconds { get; set; } = 900;
}

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollSeconds { get; set; } = 10;
    public string InstanceName { get; set; } = "worker-1";
    public int MaxRunsPerCycle { get; set; } = 10;
}
