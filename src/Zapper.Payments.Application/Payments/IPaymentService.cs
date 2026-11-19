namespace Zapper.Payments.Application.Payments;

public interface IPaymentService
{
    Task<PaymentResponseDto> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentResponseDto?> GetPaymentAsync(
        string appPaymentId,
        CancellationToken cancellationToken = default);
}