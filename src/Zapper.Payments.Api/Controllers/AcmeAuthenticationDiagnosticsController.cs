
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zapper.Payments.Api.Authentication;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/diagnostics/acme/authentication")]
[Authorize(Policy = AcmeAuthenticationDefaults.Policy)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AcmeAuthenticationDiagnosticsController
    : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public AcmeAuthenticationDiagnosticsController(
        IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Verify()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(new
        {
            authenticated = true,
            authenticationScheme =
                AcmeAuthenticationDefaults.Scheme,

            message =
                "Acme notification authentication successful."
        });
    }
}
