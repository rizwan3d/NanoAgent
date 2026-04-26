using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Tools;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class HeadlessBrowserServiceTests : IDisposable
{
    private readonly List<string> _pathsToDelete = [];

    [Fact]
    public async Task RunAsync_Should_InvokeBrowserDumpDom_AndExtractRenderedText()
    {
        FakeProcessRunner processRunner = new(request =>
        {
            request.Arguments.Should().Contain("--dump-dom");
            request.Arguments.Should().Contain("--headless=new");
            request.Arguments.Should().Contain("--window-size=800,600");
            request.Arguments.Should().Contain("--virtual-time-budget=500");
            request.Arguments.Should().Contain("https://example.com");

            return new ProcessExecutionResult(
                0,
                """
                <!doctype html>
                <html>
                  <head><title>Example</title></head>
                  <body>
                    <h1>Rendered</h1>
                    <script>ignored()</script>
                    <p>Visible text</p>
                  </body>
                </html>
                """,
                string.Empty);
        });

        HeadlessBrowserService sut = new(processRunner, browserExecutablePath: "browser");

        HeadlessBrowserResult result = await sut.RunAsync(
            new HeadlessBrowserRequest(
                "https://example.com",
                "medium",
                800,
                600,
                500,
                5000,
                CaptureScreenshot: false,
                IncludeHtml: true),
            "session_1",
            CancellationToken.None);

        result.Browser.Should().Be("browser");
        result.Title.Should().Be("Example");
        result.Text.Should().Contain("Rendered");
        result.Text.Should().Contain("Visible text");
        result.Text.Should().NotContain("ignored");
        result.Html.Should().Contain("<title>Example</title>");
        result.Screenshot.Should().BeNull();
        processRunner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_Should_SaveScreenshot_When_Requested()
    {
        FakeProcessRunner processRunner = new(request =>
        {
            if (request.Arguments.Any(argument => argument == "--dump-dom"))
            {
                return new ProcessExecutionResult(
                    0,
                    "<html><head><title>Shot</title></head><body>Screen</body></html>",
                    string.Empty);
            }

            string screenshotArgument = request.Arguments.Single(argument =>
                argument.StartsWith("--screenshot=", StringComparison.Ordinal));
            string screenshotPath = screenshotArgument["--screenshot=".Length..];
            _pathsToDelete.Add(Path.GetDirectoryName(screenshotPath)!);
            File.WriteAllBytes(screenshotPath, [1, 2, 3, 4]);

            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        });

        HeadlessBrowserService sut = new(processRunner, browserExecutablePath: "browser");

        HeadlessBrowserResult result = await sut.RunAsync(
            new HeadlessBrowserRequest(
                "https://example.com",
                "short",
                1024,
                768,
                0,
                5000,
                CaptureScreenshot: true,
                IncludeHtml: false),
            "session_2",
            CancellationToken.None);

        result.Screenshot.Should().NotBeNull();
        result.Screenshot!.ByteCount.Should().Be(4);
        result.Screenshot.ViewportWidth.Should().Be(1024);
        result.Screenshot.ViewportHeight.Should().Be(768);
        File.Exists(result.Screenshot.Path).Should().BeTrue();
        processRunner.Requests.Should().HaveCount(2);
        processRunner.Requests[1].Arguments.Should().Contain(argument =>
            argument.StartsWith("--screenshot=", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (string path in _pathsToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessExecutionRequest, ProcessExecutionResult> _handler;

        public FakeProcessRunner(Func<ProcessExecutionRequest, ProcessExecutionResult> handler)
        {
            _handler = handler;
        }

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }
}
