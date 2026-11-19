namespace Zapper.Payments.Application.Payments;

public class PaymentResponseDto
{
    public long PaymentId { get; set; }

    public string AppPaymentId { get; set; } = null!;

    public Guid TransactionId { get; set; }

    public string OrderNumber { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long Amount { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string? AcmeResponseCode { get; set; }

    public string? AcmeResponseDescription { get; set; }
}