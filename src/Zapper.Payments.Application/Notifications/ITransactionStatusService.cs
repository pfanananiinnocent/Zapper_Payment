
using Zapper.Payments.Application.Notifications.Contracts;

namespace Zapper.Payments.Application.Notifications;

public interface ITransactionStatusService
{
    Task<bool> ProcessAsync(
        AcmeTransactionStatusRequest request,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
