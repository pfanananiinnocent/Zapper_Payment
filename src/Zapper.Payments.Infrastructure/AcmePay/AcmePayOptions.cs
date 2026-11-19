namespace Zapper.Payments.Infrastructure.AcmePay;

public class AcmePayOptions
{
    public const string SectionName = "AcmePay";

    public string BaseUrl { get; set; } = null!;

    public string ApiKey { get; set; } = null!;

    public string MerchantId { get; set; } = null!;

    public int TimeoutSeconds { get; set; } = 35;
}