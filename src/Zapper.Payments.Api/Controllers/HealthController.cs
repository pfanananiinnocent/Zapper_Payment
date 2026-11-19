using Microsoft.AspNetCore.Mvc;

namespace Zapper.Payments.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            Status = "Healthy",
            Application = "Zapper Payments API",
            TimestampUtc = DateTime.Now
        });
    }
}