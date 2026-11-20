
namespace Zapper.Payments.Api.Authentication;

public static class AcmeAuthenticationDefaults
{
    public const string Scheme = "AcmeBasic";

    public const string Policy = "AcmePaymentAdvice";

    public const string ConfigurationSection =
        "AcmePay:Notifications:BasicAuthentication";

    public const string Realm = "AcmePaymentAdvice";
}
