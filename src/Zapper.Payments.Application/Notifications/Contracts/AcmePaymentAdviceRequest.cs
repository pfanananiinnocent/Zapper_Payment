
using System.Text.Json.Serialization;

namespace Zapper.Payments.Application.Notifications.Contracts;

public sealed class AcmePaymentAdviceRequest
{
    [JsonPropertyName("RequestType")]
    public string? RequestType { get; init; }

    [JsonPropertyName("MerchantID")]
    public string? MerchantId { get; init; }

    [JsonPropertyName("TransactionID")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("TransactionDateTime")]
    public string? TransactionDateTime { get; init; }

    [JsonPropertyName("OrderNumber")]
    public string? OrderNumber { get; init; }

    [JsonPropertyName("ReconID")]
    public string? ReconId { get; init; }

    [JsonPropertyName("RetrievalReferenceNumber")]
    public string? RetrievalReferenceNumber { get; init; }

    [JsonPropertyName("SystemsTraceAuditNumber")]
    public string? SystemsTraceAuditNumber { get; init; }

    [JsonPropertyName("Amount")]
    public long? Amount { get; init; }

    [JsonPropertyName("CurrencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("PaymentService")]
    public string? PaymentService { get; init; }

    [JsonPropertyName("TransactionStatus")]
    public string? TransactionStatus { get; init; }

    [JsonPropertyName("ResponseSource")]
    public string? ResponseSource { get; init; }

    [JsonPropertyName("ResponseCode")]
    public string? ResponseCode { get; init; }

    [JsonPropertyName("ResponseDescription")]
    public string? ResponseDescription { get; init; }

    [JsonPropertyName("AuthCode")]
    public string? AuthCode { get; init; }

    [JsonPropertyName("MaskedCardNumber")]
    public string? MaskedCardNumber { get; init; }

    [JsonPropertyName("CommProtocol")]
    public string? CommProtocol { get; init; }
}
