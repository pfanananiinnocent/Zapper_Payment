namespace Zapper.Payments.Domain.Entities;

public class AcmeNotification
{
    public long Id { get; set; }

    // Payment advice or TransactionStatus.
    public string NotificationType { get; set; } = null!;

    // Acme transactionUUID, where supplied.
    public string? ExternalEventId { get; set; }

    // A deterministic hash used for deduplication.
    public string Fingerprint { get; set; } = null!;

    public string PayloadJson { get; set; } = null!;
    public bool IsProcessed { get; set; }
    public string? ProcessingError { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}