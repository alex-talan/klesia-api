using Microsoft.AspNetCore.Mvc;

namespace IntegrationTests;

[ApiController]
[Route("test")]
public sealed class TestExceptionController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw()
    {
        throw new InvalidOperationException("SENSITIVE_TEST_EXCEPTION_TEXT");
    }
}
