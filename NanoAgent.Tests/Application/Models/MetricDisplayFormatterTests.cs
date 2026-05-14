using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class MetricDisplayFormatterTests
{
    [Theory]
    [InlineData(0, "1s")]
    [InlineData(1, "1s")]
    [InlineData(29, "29s")]
    [InlineData(30, "30s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m 0s")]
    [InlineData(61, "1m 1s")]
    [InlineData(119, "1m 59s")]
    [InlineData(120, "2m 0s")]
    [InlineData(3599, "59m 59s")]
    [InlineData(3600, "1h 0m 0s")]
    [InlineData(3661, "1h 1m 1s")]
    [InlineData(86399, "23h 59m 59s")]
    public void FormatElapsed_Should_ReturnExpectedFormat(int totalSeconds, string expected)
    {
        string result = MetricDisplayFormatter.FormatElapsed(TimeSpan.FromSeconds(totalSeconds));

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(500, "500")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1500, "1.5k")]
    [InlineData(1999, "2k")]
    [InlineData(10000, "10k")]
    [InlineData(10500, "11k")]
    [InlineData(100000, "100k")]
    [InlineData(-1, "0")]
    [InlineData(-100, "0")]
    public void FormatEstimatedTokens_Should_ReturnExpectedFormat(int tokens, string expected)
    {
        string result = MetricDisplayFormatter.FormatEstimatedTokens(tokens);

        result.Should().Be(expected);
    }

    [Fact]
    public void FormatEstimatedOutputMetric_Should_ReturnCombinedFormat()
    {
        string result = MetricDisplayFormatter.FormatEstimatedOutputMetric(
            TimeSpan.FromSeconds(30),
            1500);

        result.Should().Be("(30s · 1.5k tokens est.)");
    }
}
