using Zapper.Payments.Application.Notifications.Contracts;

namespace Zapper.Payments.Application.Notifications;

public interface IPaymentAdviceService
{
    Task<bool> ProcessAsync( AcmePaymentAdviceRequest request, CancellationToken cancellationToken = default);
}