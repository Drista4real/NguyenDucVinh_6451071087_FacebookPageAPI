using Microsoft.AspNetCore.Mvc;

namespace RetryService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet("health")]
    public ActionResult<object> GetHealth()
    {
        _logger.LogInformation("Health check requested");
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("ready")]
    public ActionResult<object> GetReady()
    {
        _logger.LogInformation("Readiness check requested");
        return Ok(new { status = "ready", service = "retry-service", timestamp = DateTime.UtcNow });
    }
}
