
using System.Security.Cryptography;
using System.Text;

namespace Zapper.Payments.Api.Authentication;

public static class AcmeWebhookSignatureValidator
{
    public static bool IsValid(
        byte[] body,
        string? signature,
        string? secret)
    {
        if (string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        byte[] suppliedSignature;

        try
        {
            suppliedSignature =
                Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            body);

        return CryptographicOperations.FixedTimeEquals(
            expectedSignature,
            suppliedSignature);
    }
}
