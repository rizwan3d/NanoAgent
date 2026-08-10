using FluentAssertions;
using StemCode.Application.Models;
using StemCode.Application.Tools.Models;
using StemCode.Application.Tools.Serialization;

namespace StemCode.Tests.Application.Tools.Serialization;

public sealed class ToolResultFactoryTests
{
    [Fact]
    public void Success_Should_NotEscapeApostrophesInFileReadContent()
    {
        const string content = "if (!trimmedInput.StartsWith('!'))";

        ToolResult result = ToolResultFactory.Success(
            "Read file.",
            new WorkspaceFileReadResult(
                "StemCode/Application/Backend/StemCodeBackend.cs",
                content,
                12,
                12,
                100,
                false,
                null,
                "abc123",
                "utf-8"),
            ToolJsonContext.Default.WorkspaceFileReadResult);

        result.JsonResult.Should().Contain(content);
        result.JsonResult.Should().NotContain("\\u0027");
    }
}
