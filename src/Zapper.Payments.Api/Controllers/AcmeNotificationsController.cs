
using System.Text;
using System.Text.Json;
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

    private readonly ITransactionStatusService
        _transactionStatusService;

    private readonly IConfiguration _configuration;

    public AcmeNotificationsController(
        IPaymentAdviceService paymentAdviceService,
        ITransactionStatusService transactionStatusService,
        IConfiguration configuration)
    {
        _paymentAdviceService = paymentAdviceService;

        _transactionStatusService =
            transactionStatusService;

        _configuration = configuration;
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

        return Ok();
    }

    [HttpPost("transaction-status")]
    [AllowAnonymous]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> TransactionStatus(
        CancellationToken cancellationToken)
    {
        // Read the exact request body bytes.

        using var stream = new MemoryStream();

        await Request.Body.CopyToAsync(
            stream,
            cancellationToken);

        var body = stream.ToArray();

        // Verify Acme's HMAC signature.

        var signatures = Request.Headers["x-signature"];

        if (signatures.Count != 1
            || !AcmeWebhookSignatureValidator.IsValid(
                body,
                signatures[0],
                _configuration["ACME_WEBHOOK_SECRET"]))
        {
            return Unauthorized();
        }

        // Deserialize only after successful verification.

        AcmeTransactionStatusRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<
                AcmeTransactionStatusRequest>(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (request is null)
        {
            return BadRequest();
        }

        var processed =
            await _transactionStatusService.ProcessAsync(
                request,
                Encoding.UTF8.GetString(body),
                cancellationToken);

        if (!processed)
        {
            return BadRequest(new
            {
                message =
                    "Transaction status could not be processed."
            });
        }

        return Ok();
    }
}
