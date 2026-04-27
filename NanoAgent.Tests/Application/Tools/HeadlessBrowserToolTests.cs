using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Services;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Tools;

public sealed class HeadlessBrowserToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_UrlIsMissing()
    {
        HeadlessBrowserTool sut = new(Mock.Of<IHeadlessBrowserService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'url'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_UrlSchemeIsUnsupported()
    {
        HeadlessBrowserTool sut = new(Mock.Of<IHeadlessBrowserService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "url": "file:///C:/secret.txt" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("absolute http or https URL");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResult_When_RequestIsValid()
    {
        Mock<IHeadlessBrowserService> headlessBrowserService = new(MockBehavior.Strict);
        headlessBrowserService
            .Setup(service => service.RunAsync(
                It.Is<HeadlessBrowserRequest>(request =>
                    request.Url == "https://example.com" &&
                    request.ResponseLength == "short" &&
                    request.ViewportWidth == 800 &&
                    request.ViewportHeight == 600 &&
                    request.WaitMilliseconds == 250 &&
                    request.CaptureScreenshot &&
                    request.ScreenshotRetention == HeadlessBrowserScreenshotRetention.Turn),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeadlessBrowserResult(
                "msedge.exe",
                "https://example.com",
                "Example",
                "Example Domain",
                14,
                null,
                128,
                new HeadlessBrowserScreenshotResult(
                    Path.Combine(Path.GetTempPath(), "shot.png"),
                    Path.GetTempPath(),
                    HeadlessBrowserScreenshotRetention.Turn,
                    1024,
                    800,
                    600),
                []));

        HeadlessBrowserTool sut = new(headlessBrowserService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "url": "https://example.com",
                  "response_length": "short",
                  "viewport_width": 800,
                  "viewport_height": 600,
                  "wait_ms": 250,
                  "screenshot_retention": "turn",
                  "capture_screenshot": true
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("Example Domain");
        result.RenderPayload!.Text.Should().Contain("Screenshot:");
        result.RenderPayload.Text.Should().Contain("Screenshot directory:");
        result.RenderPayload.Text.Should().Contain("Screenshot retention: turn");
        headlessBrowserService.VerifyAll();
    }

    [Fact]
    public void PermissionRequirements_Should_RegisterAsWebfetchBrowserTool()
    {
        ToolRegistry registry = new(
            [new HeadlessBrowserTool(Mock.Of<IHeadlessBrowserService>())],
            new ToolPermissionParser());

        bool found = registry.TryResolve(
            AgentToolNames.HeadlessBrowser,
            out ToolRegistration? registration);

        found.Should().BeTrue();
        registration.Should().NotBeNull();
        registration!.PermissionPolicy.ToolTags.Should().Contain(["webfetch", "browser"]);
        registration.PermissionPolicy.WebRequest.Should().NotBeNull();
        registration.PermissionPolicy.WebRequest!.RequestArgumentName.Should().Be("url");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.HeadlessBrowser,
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
