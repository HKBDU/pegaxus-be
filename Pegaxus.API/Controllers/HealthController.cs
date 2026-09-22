using Microsoft.AspNetCore.Mvc;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
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