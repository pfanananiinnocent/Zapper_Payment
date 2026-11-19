namespace Zapper.Payments.Application.Exceptions;

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message)
        : base(message)
    {
    }
}