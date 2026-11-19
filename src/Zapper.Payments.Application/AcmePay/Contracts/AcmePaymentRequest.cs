using System.Text.Json.Serialization;

namespace Zapper.Payments.Application.Payments.AcmePay.Contracts;

public class AcmePaymentRequest
{
    [JsonPropertyName("MessageHeader")]
    public AcmeMessageHeader MessageHeader { get; set; } = new();

    [JsonPropertyName("PaymentRequest")]
    public AcmePaymentRequestBody PaymentRequest { get; set; } = new();
}

public class AcmeMessageHeader
{
    [JsonPropertyName("MessageDateTime")]
    public string MessageDateTime { get; set; } = null!;

    [JsonPropertyName("MessageID")]
    public string MessageID { get; set; } = null!;

    [JsonPropertyName("Channel")]
    public string Channel { get; set; } = "App";
}

public class AcmePaymentRequestBody
{
    [JsonPropertyName("MerchantID")]
    public string MerchantID { get; set; } = null!;

    [JsonPropertyName("TransactionID")]
    public string TransactionID { get; set; } = null!;

    [JsonPropertyName("OrderNumber")]
    public string OrderNumber { get; set; } = null!;

    [JsonPropertyName("Amount")]
    public long Amount { get; set; }

    [JsonPropertyName("CurrencyCode")]
    public string CurrencyCode { get; set; } = "ZAR";

    [JsonPropertyName("PaymentService")]
    public string PaymentService { get; set; } = "CardNotPresent";

    [JsonPropertyName("Card")]
    public AcmeCard Card { get; set; } = new();

    [JsonPropertyName("CardOnFileScenario")]
    public string CardOnFileScenario { get; set; } = "CIT";

    [JsonPropertyName("SubMerchantID")]
    public string SubMerchantID { get; set; } = null!;

    [JsonPropertyName("MerchantCategoryCode")]
    public string MerchantCategoryCode { get; set; } = null!;

    [JsonPropertyName("CardAcceptorName")]
    public string CardAcceptorName { get; set; } = null!;

    [JsonPropertyName("Email")]
    public string Email { get; set; } = null!;
}

public class AcmeCard
{
    [JsonPropertyName("Token")]
    public string Token { get; set; } = null!;
}