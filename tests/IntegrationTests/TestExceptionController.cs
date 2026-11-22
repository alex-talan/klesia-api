using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationTests;

[ApiController]
[Route("test")]
public sealed class TestExceptionController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("throw")]
    public IActionResult Throw()
    {
        throw new InvalidOperationException("SENSITIVE_TEST_EXCEPTION_TEXT");
    }

    [AllowAnonymous]
    [HttpGet("https")]
    public string HttpsScheme()
    {
        return HttpContext.Request.Scheme;
    }

    [Authorize]
    [HttpGet("authenticated")]
    public IActionResult Authenticated()
    {
        return Ok(new { authenticated = true });
    }
}
