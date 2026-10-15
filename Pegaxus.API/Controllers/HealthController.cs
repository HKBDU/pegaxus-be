using Microsoft.AspNetCore.Mvc;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Check API health.
    /// </summary>
    /// <remarks>Reports API availability, service name, timestamp, and version. Does not check databases or external services.</remarks>
    /// <returns>200 OK with a health snapshot, without the standard response envelope.</returns>
    [HttpGet]
    public IActionResult Check()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "Pegaxus Backend API",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }
}
