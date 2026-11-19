namespace Zapper.AcmePay.Stub.Instrumentation;

public sealed class AcmeMetricsOptions
{
    public const string SectionName = "AcmeMetrics";

    public bool Enabled { get; set; } = true;

    public int RequestsPerSecondLimit { get; set; } = 20;

    public int ExpectedMaxConcurrency { get; set; } = 10;

    public int PaymentDelayMilliseconds { get; set; } = 0;

    public int RttqDelayMilliseconds { get; set; } = 0;
}