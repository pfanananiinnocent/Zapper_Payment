using Microsoft.AspNetCore.Mvc;
using Zapper.Payments.Application.Exceptions;
using Zapper.Payments.Application.Payments;
using Zapper.Payments.Application.Payments.Reconciliation;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    private readonly IPaymentReconciliationService _reconciliationService;

    private readonly IWebHostEnvironment _environment;

    public PaymentsController( IPaymentService paymentService, IPaymentReconciliationService reconciliationService, IWebHostEnvironment environment)
    {
        _paymentService = paymentService;
        _reconciliationService = reconciliationService;
        _environment = environment;
    }

    [HttpPost]
    [ProducesResponseType( typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType( typeof(PaymentResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(PaymentResponseDto),
    StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _paymentService.CreatePaymentAsync( request, cancellationToken);

            if (result.Status == "NotDispatched")
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    result);
            }

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
    [ProducesResponseType( typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment( string appPaymentId,CancellationToken cancellationToken)
    {
        var result = await _paymentService.GetPaymentAsync( appPaymentId, cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost("{appPaymentId}/reconcile")]
    [ProducesResponseType(typeof(ReconciliationResult),StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReconciliationResult),StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReconcilePayment(string appPaymentId, CancellationToken cancellationToken)
    {
        // Manual reconciliation is exposed only for
        // development and assessment verification.
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var result =
            await _reconciliationService.ReconcileAsync(
                appPaymentId,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        if (!result.IsResolved)
        {
            return AcceptedAtAction(
                nameof(GetPayment),
                new { appPaymentId },
                result);
        }

        return Ok(result);
    }
}