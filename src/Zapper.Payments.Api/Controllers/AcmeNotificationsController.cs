
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zapper.Payments.Api.Authentication;
using Zapper.Payments.Application.Notifications;
using Zapper.Payments.Application.Notifications.Contracts;
using Zapper.Payments.Application.Notifications.Validation;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/notifications/acme")]
public sealed class AcmeNotificationsController
    : ControllerBase
{
    private readonly IPaymentAdviceService _paymentAdviceService;

    public AcmeNotificationsController(
        IPaymentAdviceService paymentAdviceService)
    {
        _paymentAdviceService = paymentAdviceService;
    }

    [HttpPost("payment-advice")]
    [Authorize(Policy = AcmeAuthenticationDefaults.Policy)]
    public async Task<IActionResult> PaymentAdvice(
        [FromBody] AcmePaymentAdviceRequest request,
        CancellationToken cancellationToken)
    {
        var validation =
            AcmePaymentAdviceValidator.Validate(request);

        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                message = "Invalid payment advice notification.",
                errors = validation.Errors
            });
        }

        var processed =
            await _paymentAdviceService.ProcessAsync(
                request,
                cancellationToken);

        if (!processed)
        {
            return BadRequest(new
            {
                message =
                    "Payment advice could not be processed."
            });
        }

        // Acme expects HTTP 200 acknowledgement.

        return Ok();
    }
}
