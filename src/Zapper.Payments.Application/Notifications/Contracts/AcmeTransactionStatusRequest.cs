
using System.Text.Json.Serialization;

namespace Zapper.Payments.Application.Notifications.Contracts;

public sealed class AcmeTransactionStatusRequest
{
    [JsonPropertyName("transactionUUID")]
    public string? TransactionUuid { get; init; }

    [JsonPropertyName("transactionType")]
    public string? TransactionType { get; init; }

    [JsonPropertyName("transactionDateTime")]
    public string? TransactionDateTime { get; init; }

    [JsonPropertyName("rrn")]
    public string? Rrn { get; init; }

    [JsonPropertyName("transactionAmount")]
    public long? TransactionAmount { get; init; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("txResultCode")]
    public string? TxResultCode { get; init; }

    [JsonPropertyName("isApproved")]
    public bool? IsApproved { get; init; }
}
