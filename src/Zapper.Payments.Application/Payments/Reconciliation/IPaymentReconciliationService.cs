namespace Zapper.Payments.Application.Payments.Reconciliation;

public interface IPaymentReconciliationService
{
    Task<ReconciliationResult?> ReconcileAsync(
        string appPaymentId,
        CancellationToken cancellationToken = default);
}