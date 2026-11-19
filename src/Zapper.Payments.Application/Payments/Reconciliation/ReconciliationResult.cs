namespace Zapper.Payments.Application.Payments.Reconciliation;

public sealed class ReconciliationResult
{
    public long PaymentId { get; init; }

    public string AppPaymentId { get; init; } = null!;

    public Guid TransactionId { get; init; }

    public string PreviousStatus { get; init; } = null!;

    public string CurrentStatus { get; init; } = null!;

    public bool StatusChanged { get; init; }

    public bool IsResolved { get; init; }

    public int? AcmeHttpStatusCode { get; init; }

    public string? AcmeResponseCode { get; init; }

    public double? RetryAfterSeconds { get; init; }

    public string Message { get; init; } = null!;
}