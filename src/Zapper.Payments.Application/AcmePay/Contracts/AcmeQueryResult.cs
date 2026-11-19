using System.Net;

namespace Zapper.Payments.Application.Payments.AcmePay.Contracts;

public sealed class AcmeQueryResult
{
    public HttpStatusCode StatusCode { get; init; }

    public AcmePaymentResponse? Response { get; init; }

    public TimeSpan? RetryAfter { get; init; }

    public bool IsSuccessful =>
        (int)StatusCode >= 200 &&
        (int)StatusCode <= 299;

    public bool IsNotFound =>
        StatusCode == HttpStatusCode.NotFound;

    public bool IsRateLimited =>
        StatusCode == HttpStatusCode.TooManyRequests;
}