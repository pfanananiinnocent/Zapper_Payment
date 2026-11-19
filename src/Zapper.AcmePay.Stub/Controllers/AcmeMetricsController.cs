using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zapper.AcmePay.Stub.Instrumentation;

namespace Zapper.AcmePay.Stub.Controllers;

[ApiController]
[Route("api/diagnostics/metrics")]
public sealed class AcmeMetricsController : ControllerBase
{
    private readonly AcmeRequestMetrics _metrics;

    private readonly AcmeMetricsOptions _options;

    private readonly IWebHostEnvironment _environment;

    public AcmeMetricsController(
        AcmeRequestMetrics metrics,
        IOptions<AcmeMetricsOptions> options,
        IWebHostEnvironment environment)
    {
        _metrics = metrics;
        _options = options.Value;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult GetMetrics()
    {
        if (!_environment.IsDevelopment() ||
            !_options.Enabled)
        {
            return NotFound();
        }

        return Ok(_metrics.GetSnapshot());
    }

    [HttpPost("reset")]
    public IActionResult ResetMetrics()
    {
        if (!_environment.IsDevelopment() ||
            !_options.Enabled)
        {
            return NotFound();
        }

        if (!_metrics.TryReset())
        {
            return Conflict(new
            {
                Message = "Cannot reset metrics while " +
                          "requests are still active."
            });
        }

        return Ok(new
        {
            Message = "Acme metrics reset successfully.",
            ResetAtUtc = DateTimeOffset.UtcNow
        });
    }
}