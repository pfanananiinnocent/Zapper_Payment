namespace Zapper.Payments.Infrastructure.BackgroundServices;

public sealed class ReconciliationOptions
{
    public const string SectionName ="Reconciliation";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 20;

    public int RetryDelaySeconds { get; set; } = 60;

    public int InitialDelaySeconds { get; set; } = 10;
}