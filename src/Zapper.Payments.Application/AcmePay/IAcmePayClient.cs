using Zapper.Payments.Application.Payments.AcmePay.Contracts;

namespace Zapper.Payments.Application.Payments.AcmePay;

public interface IAcmePayClient
{
    Task<AcmePaymentResponse> SubmitPaymentAsync(
        AcmePaymentRequest request,
        CancellationToken cancellationToken = default);
}