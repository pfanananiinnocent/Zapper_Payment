namespace Zapper.Payments.Application.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message)
        : base(message)
    {
    }
}