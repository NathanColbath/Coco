using Microsoft.AspNetCore.Mvc;

namespace ComicStudio.Api.Features.Platform;

public record HealthResponse(string Status, string Service, DateTimeOffset Time);

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("ok", "ComicStudio.Api", DateTimeOffset.UtcNow));
}
