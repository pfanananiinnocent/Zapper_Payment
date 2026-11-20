
namespace Zapper.Payments.Application.Notifications.Validation;

public sealed class AcmePaymentAdviceValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();

    public Guid? MerchantId { get; init; }

    public Guid? TransactionId { get; init; }

    public DateTimeOffset? TransactionDateTimeUtc { get; init; }

    public string CurrencyCode { get; init; } = "ZAR";
}
