using System.Text.Json.Serialization;

namespace Zapper.Payments.Application.Payments.AcmePay.Contracts;

public class AcmePaymentResponse
{
    [JsonPropertyName("MessageHeader")]
    public AcmeResponseHeader? MessageHeader { get; set; }

    [JsonPropertyName("PaymentResponse")]
    public AcmePaymentResponseBody? PaymentResponse { get; set; }
}

public class AcmeResponseHeader
{
    [JsonPropertyName("MessageDateTime")]
    public string? MessageDateTime { get; set; }

    [JsonPropertyName("MessageID")]
    public string? MessageID { get; set; }

    [JsonPropertyName("Channel")]
    public string? Channel { get; set; }
}

public class AcmePaymentResponseBody
{
    [JsonPropertyName("TransactionID")]
    public string? TransactionID { get; set; }

    [JsonPropertyName("OrderNumber")]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("TransactionStatus")]
    public string? TransactionStatus { get; set; }

    [JsonPropertyName("ResponseDetail")]
    public AcmeResponseDetail? ResponseDetail { get; set; }

    [JsonPropertyName("TransactionDateTime")]
    public string? TransactionDateTime { get; set; }

    [JsonPropertyName("ReconID")]
    public string? ReconID { get; set; }

    [JsonPropertyName("RetrievalReferenceNumber")]
    public string? RetrievalReferenceNumber { get; set; }

    [JsonPropertyName("AuthCode")]
    public string? AuthCode { get; set; }

    [JsonPropertyName("AuthAmount")]
    public long? AuthAmount { get; set; }
}

public class AcmeResponseDetail
{
    [JsonPropertyName("Source")]
    public string? Source { get; set; }

    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("ClientMessage")]
    public string? ClientMessage { get; set; }
}