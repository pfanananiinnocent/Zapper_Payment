namespace Zapper.Payments.Domain.Enums;

public enum PaymentEventType
{
    PaymentSubmitted = 1,
    PaymentApproved = 2,
    PaymentDeclined = 3,
    PaymentPending = 4,
    PaymentReversed = 5,
    PaymentRefunded = 6,
    ReconciliationUpdated = 7
}