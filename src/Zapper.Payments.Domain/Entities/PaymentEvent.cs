using Zapper.Payments.Domain.Enums;

namespace Zapper.Payments.Domain.Entities;

public class PaymentEvent
{
    public long Id { get; set; }
    public long PaymentId { get; set; }

    public PaymentEventType EventType { get; set; }
    public PaymentStatus? PreviousStatus { get; set; }
    public PaymentStatus NewStatus { get; set; }

    public long? Amount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Description { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }

    public Payment Payment { get; set; } = null!;
}