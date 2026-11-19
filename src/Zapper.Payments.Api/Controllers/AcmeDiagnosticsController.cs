using Microsoft.AspNetCore.Mvc;
using Zapper.Payments.Application.Payments.AcmePay;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/diagnostics/acme")]
public class AcmeDiagnosticsController : ControllerBase
{
    private readonly IAcmePayClient _acmePayClient;
    private readonly IWebHostEnvironment _environment;

    public AcmeDiagnosticsController(
        IAcmePayClient acmePayClient,
        IWebHostEnvironment environment)
    {
        _acmePayClient = acmePayClient;
        _environment = environment;
    }

    [HttpGet("transactions/{transactionId:guid}")]
    public async Task<IActionResult> QueryTransaction(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        // Diagnostic endpoint is only available
        // in the local development environment.
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            var result =
                await _acmePayClient.QueryTransactionAsync(
                    transactionId,
                    cancellationToken);

            return StatusCode(
                (int)result.StatusCode,
                new
                {
                    HttpStatusCode = (int)result.StatusCode,

                    result.IsSuccessful,

                    result.IsNotFound,

                    result.IsRateLimited,

                    RetryAfterSeconds =
                        result.RetryAfter?.TotalSeconds,

                    AcmeResponse = result.Response
                });
        }
        catch (HttpRequestException)
        {
            return Problem(
                title: "Acme communication failure",
                detail: "Unable to communicate with Acme Pay.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                title: "Acme request timed out",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException
                  or System.Text.Json.JsonException
                  or NotSupportedException)
        {
            return Problem(
                title: "Invalid Acme response",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}