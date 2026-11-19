namespace Zapper.Payments.Infrastructure.AcmePay.Resilience;

public sealed class AcmeResilienceOptions
{
    public const string SectionName = "AcmeResilience";

    public int RequestsPerSecond { get; set; } = 18;

    public int MaxConcurrentRequests { get; set; } = 10;

    public int QueueLimit { get; set; } = 100;

    public int QueueTimeoutSeconds { get; set; } = 5;

    public int DefaultRetryAfterSeconds { get; set; } = 2;
}