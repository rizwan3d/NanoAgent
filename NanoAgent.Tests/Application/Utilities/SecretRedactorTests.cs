using FluentAssertions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("normal text without secrets", "normal text without secrets")]
    public void Redact_Should_Return_Input_When_NoSecrets(string? input, string expected)
    {
        string result = SecretRedactor.Redact(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsEnvironmentFilePath_Should_ReturnTrue_For_DotEnv()
    {
        SecretRedactor.IsEnvironmentFilePath(".env").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("/path/to/.env").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath(".env.production").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("/path/.env.local").Should().BeTrue();
    }

    [Fact]
    public void IsEnvironmentFilePath_Should_ReturnFalse_For_NonDotEnv()
    {
        SecretRedactor.IsEnvironmentFilePath(null).Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath("").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath("  ").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath("config.json").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath(".envfile").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath("environment").Should().BeFalse();
    }

    [Fact]
    public void RedactEnvironmentFileContent_Should_Redact_Assignments()
    {
        bool originalValue = SecretRedactor.IsEnabled;
        SecretRedactor.IsEnabled = true;

        try
        {
            string result = SecretRedactor.RedactEnvironmentFileContent(
                "DATABASE_URL=postgres://user:pass@localhost/db");

            result.Should().Contain("<redacted>");
        }
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }

    [Fact]
    public void RedactEnvironmentFileContent_Should_Return_Empty_When_Null()
    {
        string result = SecretRedactor.RedactEnvironmentFileContent(null);

        result.Should().Be("");
    }
}
