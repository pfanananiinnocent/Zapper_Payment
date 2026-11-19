using Microsoft.AspNetCore.Mvc;
using Zapper.Payments.Application.Exceptions;
using Zapper.Payments.Application.Payments;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(
        IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(PaymentResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _paymentService.CreatePaymentAsync(
                    request,
                    cancellationToken);

            if (result.Status is
                "Pending" or "PendingReconciliation")
            {
                return AcceptedAtAction(
                    nameof(GetPayment),
                    new { appPaymentId = result.AppPaymentId },
                    result);
            }

            return Ok(result);
        }
        catch (PaymentValidationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PaymentConflictException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpGet("{appPaymentId}")]
    [ProducesResponseType(
        typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(
        string appPaymentId,
        CancellationToken cancellationToken)
    {
        var result =
            await _paymentService.GetPaymentAsync(
                appPaymentId,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}