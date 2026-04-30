using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ToolResultRedactionTests
{
    [Fact]
    public void Constructor_Should_RedactSecretsFromMessageJsonAndRenderPayload()
    {
        ToolResult result = ToolResult.Success(
            "Shell printed sk-abcdefghijklmnopqrstuvwxyz123456",
            """{"StandardOutput":"password=hunter2\nBearer abcdefghijklmnopqrstuvwxyz"}""",
            new ToolRenderPayload(
                "Output",
                "github_pat_abcdefghijklmnopqrstuvwxyz1234567890"));

        result.Message.Should().Contain("<redacted>");
        result.Message.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz");
        result.JsonResult.Should().Contain("password=<redacted>");
        result.JsonResult.Should().Contain("Bearer <redacted>");
        result.JsonResult.Should().NotContain("hunter2");
        result.RenderPayload!.Text.Should().Be("<redacted>");
    }
}
