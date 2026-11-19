using Zapper.Payments.Domain.Enums;

namespace Zapper.Payments.Domain.Entities;

public class Payment
{
    public long Id { get; set; }

    public int CustomerId { get; set; }
    public int StoredCardId { get; set; }
    public int SubMerchantId { get; set; }

    // The identifier supplied by the Zapper app.
    public string AppPaymentId { get; set; } = null!;

    // Stable identifiers used for Acme requests and reconciliation.
    public Guid TransactionId { get; set; }
    public string OrderNumber { get; set; } = null!;

    public string? RetrievalReferenceNumber { get; set; }
    public string? AcmeReconId { get; set; }

    public long Amount { get; set; }
    public long? AuthorisedAmount { get; set; }
    public long RefundedAmount { get; set; }

    public string CurrencyCode { get; set; } = "ZAR";
    public PaymentStatus Status { get; set; }

    public string? AcmeResponseCode { get; set; }
    public string? AcmeResponseDescription { get; set; }
    public string? AuthorisationCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // SQL Server optimistic concurrency token.
    public byte[] RowVersion { get; set; } = [];

    public Customer Customer { get; set; } = null!;
    public StoredCard StoredCard { get; set; } = null!;
    public SubMerchant SubMerchant { get; set; } = null!;

    public ICollection<PaymentEvent> Events { get; set; } = [];
}