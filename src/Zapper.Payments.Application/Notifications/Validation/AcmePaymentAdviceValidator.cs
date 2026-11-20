
using System.Globalization;
using Zapper.Payments.Application.Notifications.Contracts;

namespace Zapper.Payments.Application.Notifications.Validation;

public static class AcmePaymentAdviceValidator
{
    private static readonly TimeSpan SouthAfricanOffset =
        TimeSpan.FromHours(2);

    private static readonly string[] ValidRequestTypes =
    {
        "Authorize",
        "Capture",
        "Payment"
    };

    private static readonly string[] ValidTransactionStatuses =
    {
        "Success",
        "Failure",
        "Other"
    };

    public static AcmePaymentAdviceValidationResult Validate( AcmePaymentAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        Guid? merchantId = null;
        Guid? transactionId = null;

        DateTimeOffset? transactionDateTimeUtc = null;

        // --------------------------------------------
        // Required fields
        // --------------------------------------------

        ValidateRequired( request.RequestType, "RequestType", errors);

        ValidateRequired(
            request.MerchantId,
            "MerchantID",
            errors,
            50);

        ValidateRequired(
            request.TransactionId,
            "TransactionID",
            errors,
            50);

        ValidateRequired(
            request.RetrievalReferenceNumber,
            "RetrievalReferenceNumber",
            errors,
            12);

        ValidateRequired(
            request.SystemsTraceAuditNumber,
            "SystemsTraceAuditNumber",
            errors,
            6);

        ValidateRequired(
            request.PaymentService,
            "PaymentService",
            errors);

        ValidateRequired(
            request.TransactionStatus,
            "TransactionStatus",
            errors);

        ValidateRequired(
            request.ResponseSource,
            "ResponseSource",
            errors,
            50);

        ValidateRequired(
            request.ResponseCode,
            "ResponseCode",
            errors,
            3);

        ValidateRequired(
            request.CommProtocol,
            "CommProtocol",
            errors,
            4);

        // --------------------------------------------
        // Optional fields
        // --------------------------------------------

        ValidateOptionalLength(
            request.OrderNumber,
            "OrderNumber",
            50,
            errors);

        ValidateOptionalLength(
            request.ReconId,
            "ReconID",
            50,
            errors);

        ValidateOptionalLength(
            request.ResponseDescription,
            "ResponseDescription",
            50,
            errors);

        ValidateOptionalLength(
            request.AuthCode,
            "AuthCode",
            6,
            errors);

        ValidateOptionalLength(
            request.MaskedCardNumber,
            "MaskedCardNumber",
            19,
            errors);

        // --------------------------------------------
        // Merchant identity
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.MerchantId))
        {
            if (Guid.TryParse(
                    request.MerchantId,
                    out var parsedMerchantId)
                && parsedMerchantId != Guid.Empty)
            {
                merchantId = parsedMerchantId;
            }
            else
            {
                errors.Add("MerchantID must be a valid GUID.");
            }
        }

        // --------------------------------------------
        // Transaction identity
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.TransactionId))
        {
            if (Guid.TryParse(
                    request.TransactionId,
                    out var parsedTransactionId)
                && parsedTransactionId != Guid.Empty)
            {
                transactionId = parsedTransactionId;
            }
            else
            {
                errors.Add(
                    "TransactionID must be a valid GUID.");
            }
        }

        // --------------------------------------------
        // Request type
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.RequestType)
            && !ValidRequestTypes.Contains(
                request.RequestType,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                "RequestType must be Authorize, Capture or Payment.");
        }

        // --------------------------------------------
        // Transaction status
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.TransactionStatus)
            && !ValidTransactionStatuses.Contains(
                request.TransactionStatus,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                "TransactionStatus must be Success, Failure or Other.");
        }

        // --------------------------------------------
        // Financial amount
        // --------------------------------------------

        if (!request.Amount.HasValue)
        {
            errors.Add("Amount is required.");
        }
        else if (request.Amount.Value < 0)
        {
            errors.Add("Amount cannot be negative.");
        }

        // --------------------------------------------
        // Currency
        // --------------------------------------------

        var currencyCode = request.CurrencyCode is null
            ? "ZAR"
            : request.CurrencyCode.Trim().ToUpperInvariant();

        if (currencyCode.Length != 3
            || currencyCode.Any(c => c < 'A' || c > 'Z'))
        {
            errors.Add(
                "CurrencyCode must contain three letters.");
        }

        // --------------------------------------------
        // Communication protocol
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.CommProtocol)
            && !string.Equals(
                request.CommProtocol,
                "REST",
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("CommProtocol must be REST.");
        }

        // --------------------------------------------
        // SAST timestamp conversion
        // --------------------------------------------

        if (!string.IsNullOrWhiteSpace(
                request.TransactionDateTime))
        {
            if (DateTime.TryParseExact(
                request.TransactionDateTime,
                "yyyy-MM-dd'T'HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDateTime))
            {
                var unspecifiedDateTime =
                    DateTime.SpecifyKind(
                        parsedDateTime,
                        DateTimeKind.Unspecified);

                transactionDateTimeUtc =
                    new DateTimeOffset(
                        unspecifiedDateTime,
                        SouthAfricanOffset)
                    .ToUniversalTime();
            }
            else
            {
                errors.Add(
                    "TransactionDateTime must be a valid SAST " +
                    "timestamp in yyyy-MM-ddTHH:mm:ss.fff format.");
            }
        }

        return new AcmePaymentAdviceValidationResult
        {
            Errors = errors.AsReadOnly(),
            MerchantId = merchantId,
            TransactionId = transactionId,
            TransactionDateTimeUtc = transactionDateTimeUtc,
            CurrencyCode = currencyCode
        };
    }

    private static void ValidateRequired(string? value,string fieldName,ICollection<string> errors,int? maximumLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} is required.");
            return;
        }

        if (maximumLength.HasValue
            && value.Length > maximumLength.Value)
        {
            errors.Add(
                $"{fieldName} cannot exceed " +
                $"{maximumLength.Value} characters.");
        }
    }

    private static void ValidateOptionalLength(
        string? value,
        string fieldName,
        int maximumLength,
        ICollection<string> errors)
    {
        if (value is not null
            && value.Length > maximumLength)
        {
            errors.Add(
                $"{fieldName} cannot exceed " +
                $"{maximumLength} characters.");
        }
    }
}
