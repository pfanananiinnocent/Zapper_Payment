
using System.Text.Json;
using Zapper.Payments.Application.Notifications.Contracts;
using Zapper.Payments.Application.Notifications.Validation;
using Xunit;

namespace Zapper.Payments.UnitTests.Notifications;

public sealed class AcmePaymentAdviceValidatorTests
{
    private const string ValidJson = """
    {
        "RequestType": "Payment",
        "MerchantID": "8B67BF88-BB8D-4EB5-ADFC-0F5C97C7ED67",
        "TransactionID": "2e0e1b85-bf91-45a9-b038-282e00036919",
        "TransactionDateTime": "2026-03-01T13:56:36.533",
        "OrderNumber": "MR43851259",
        "ReconID": "MR747891457",
        "RetrievalReferenceNumber": "MR43851259",
        "SystemsTraceAuditNumber": "7543",
        "Amount": 1999,
        "CurrencyCode": "ZAR",
        "PaymentService": "CardNotPresent",
        "TransactionStatus": "Success",
        "ResponseSource": "Acquirer",
        "ResponseCode": "00",
        "ResponseDescription": "Approved",
        "AuthCode": "238699",
        "MaskedCardNumber": "541333******1429",
        "CommProtocol": "REST"
    }
    """;

    [Fact]
    public void Validate_ValidPaymentAdvice_ReturnsValid()
    {
        var request = Deserialize(ValidJson);

        var result =
            AcmePaymentAdviceValidator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("ZAR", result.CurrencyCode);

        Assert.Equal(
            Guid.Parse(
                "2e0e1b85-bf91-45a9-b038-282e00036919"),
            result.TransactionId!.Value);

        Assert.Equal(1999L, request.Amount!.Value);
    }

    [Fact]
    public void Validate_SastTimestamp_ConvertsToUtc()
    {
        var request = Deserialize(ValidJson);

        var result =
            AcmePaymentAdviceValidator.Validate(request);

        var expected = new DateTimeOffset(
            2026, 3, 1,
            11, 56, 36, 533,
            TimeSpan.Zero);

        Assert.True(result.IsValid);

        Assert.Equal(
            expected,
            result.TransactionDateTimeUtc!.Value);

        Assert.Equal(
            TimeSpan.Zero,
            result.TransactionDateTimeUtc.Value.Offset);
    }

    [Fact]
    public void Validate_MissingAmount_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "\"Amount\": 1999,", "");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "Amount is required.",
            result.Errors);
    }

    [Fact]
    public void Validate_NegativeAmount_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "\"Amount\": 1999",
            "\"Amount\": -1999");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "Amount cannot be negative.",
            result.Errors);
    }

    [Fact]
    public void Validate_InvalidTransactionId_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "2e0e1b85-bf91-45a9-b038-282e00036919",
            "INVALID-ID");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "TransactionID must be a valid GUID.",
            result.Errors);
    }

    [Fact]
    public void Validate_InvalidMerchantId_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "8B67BF88-BB8D-4EB5-ADFC-0F5C97C7ED67",
            "INVALID-MERCHANT");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "MerchantID must be a valid GUID.",
            result.Errors);
    }

    [Fact]
    public void Validate_MissingCurrency_DefaultsToZar()
    {
        var json = ValidJson.Replace(
            "\"CurrencyCode\": \"ZAR\",", "");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.True(result.IsValid);
        Assert.Equal("ZAR", result.CurrencyCode);
    }

    [Fact]
    public void Validate_InvalidCurrency_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "\"CurrencyCode\": \"ZAR\"",
            "\"CurrencyCode\": \"123\"");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "CurrencyCode must contain three letters.",
            result.Errors);
    }

    [Fact]
    public void Validate_UnknownJsonFields_AreIgnored()
    {
        var json = ValidJson.Replace(
            "\"CommProtocol\": \"REST\"",
            """
            "CommProtocol": "REST",
            "FutureAcmeField": "NewValue",
            "AdditionalMetadata": {
                "Version": 2
            }
            """);

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Authorize")]
    [InlineData("Capture")]
    [InlineData("Payment")]
    public void Validate_SupportedRequestTypes_ReturnValid(
        string requestType)
    {
        var json = ValidJson.Replace(
            "\"RequestType\": \"Payment\"",
            $"\"RequestType\": \"{requestType}\"");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Refund")]
    [InlineData("Reversal")]
    [InlineData("Unsupported")]
    public void Validate_UnsupportedRequestTypes_ReturnInvalid(
        string requestType)
    {
        var json = ValidJson.Replace(
            "\"RequestType\": \"Payment\"",
            $"\"RequestType\": \"{requestType}\"");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("05")]
    [InlineData("51")]
    [InlineData("54")]
    [InlineData("91")]
    public void Validate_DeclinedAdvice_IsStructurallyValid(
        string responseCode)
    {
        var json = ValidJson.Replace(
            "\"ResponseCode\": \"00\"",
            $"\"ResponseCode\": \"{responseCode}\"");

        var request = Deserialize(json);

        var result =
            AcmePaymentAdviceValidator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(responseCode, request.ResponseCode);
    }

    [Fact]
    public void Validate_InvalidTimestamp_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "2026-03-01T13:56:36.533",
            "INVALID-DATE");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "TransactionDateTime"));
    }

    [Fact]
    public void Validate_MissingOptionalTimestamp_ReturnsValid()
    {
        var json = ValidJson.Replace(
            "\"TransactionDateTime\": " +
            "\"2026-03-01T13:56:36.533\",",
            "");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.True(result.IsValid);
        Assert.Null(result.TransactionDateTimeUtc);
    }

    [Fact]
    public void Validate_MissingResponseCode_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "\"ResponseCode\": \"00\",", "");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "ResponseCode is required.",
            result.Errors);
    }

    [Fact]
    public void Validate_ReferenceExceedsMaximumLength_ReturnsInvalid()
    {
        var json = ValidJson.Replace(
            "\"RetrievalReferenceNumber\": \"MR43851259\"",
            "\"RetrievalReferenceNumber\": \"1234567890123\"");

        var result =
            AcmePaymentAdviceValidator.Validate(
                Deserialize(json));

        Assert.False(result.IsValid);
        Assert.Contains(
            "RetrievalReferenceNumber cannot exceed 12 characters.",
            result.Errors);
    }

    private static AcmePaymentAdviceRequest Deserialize(
        string json)
    {
        return JsonSerializer.Deserialize<
            AcmePaymentAdviceRequest>(json)
            ?? throw new InvalidOperationException(
                "Unable to deserialize payment advice.");
    }
}
