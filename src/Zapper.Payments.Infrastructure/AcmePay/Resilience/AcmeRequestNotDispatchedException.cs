namespace Zapper.Payments.Infrastructure.AcmePay.Resilience;

public sealed class AcmeRequestNotDispatchedException
    : HttpRequestException
{
    public AcmeRequestNotDispatchedException(string message) : base(message)
    {
    }
}