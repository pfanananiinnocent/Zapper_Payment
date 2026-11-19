namespace Zapper.AcmePay.Stub.Instrumentation;

public sealed class AcmeMetricsSnapshot
{
    public DateTimeOffset MetricsStartedAtUtc { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public long TotalRequests { get; init; }

    public long PaymentRequests { get; init; }

    public long RttqRequests { get; init; }

    public long CompletedRequests { get; init; }

    public long AbortedRequests { get; init; }

    public int ActiveRequests { get; init; }

    public int PeakConcurrentRequests { get; init; }

    public int CurrentRollingRequestsPerSecond { get; init; }

    public int PeakRollingRequestsPerSecond { get; init; }

    public int ConfiguredRequestsPerSecondLimit { get; init; }

    public int ExpectedMaxConcurrency { get; init; }

    public long RateLimitViolations { get; init; }

    public long ConcurrencyLimitViolations { get; init; }

    public bool IsRateLimitCompliant { get; init; }

    public bool IsConcurrencyLimitCompliant { get; init; }

    public long Http2xxResponses { get; init; }

    public long Http429Responses { get; init; }

    public long Http4xxResponses { get; init; }

    public long Http5xxResponses { get; init; }

    public double AverageRequestDurationMs { get; init; }

    public double MaximumRequestDurationMs { get; init; }

    public int PaymentDelayMilliseconds { get; init; }

    public int RttqDelayMilliseconds { get; init; }
}