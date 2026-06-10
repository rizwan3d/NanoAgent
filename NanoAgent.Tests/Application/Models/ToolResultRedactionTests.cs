using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Models;

public sealed class ToolResultRedactionTests
{
    [Fact]
    public void Constructor_Should_RedactSecretsFromMessageJsonAndRenderPayload()
    {
        bool originalValue = SecretRedactor.IsEnabled;
        SecretRedactor.IsEnabled = true;

        try
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
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }
}
