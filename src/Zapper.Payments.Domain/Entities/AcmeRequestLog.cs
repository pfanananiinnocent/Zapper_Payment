namespace Zapper.Payments.Domain.Entities;

public class AcmeRequestLog
{
    public long Id { get; set; }
    public long? PaymentId { get; set; }

    public string TransactionId { get; set; } = null!;
    public string RequestType { get; set; } = null!;

    public int? HttpStatusCode { get; set; }
    public string? ResponseCode { get; set; }

    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? DurationMilliseconds { get; set; }
}