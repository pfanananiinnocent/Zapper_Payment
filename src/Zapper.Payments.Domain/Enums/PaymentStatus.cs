namespace Zapper.Payments.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Declined = 3,
    PendingReconciliation = 4,
    Reversed = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
    NotDispatched = 8
}