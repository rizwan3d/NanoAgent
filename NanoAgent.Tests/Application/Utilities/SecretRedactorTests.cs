using NanoAgent.Application.Utilities;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_Should_RemoveCommonSecretPatterns()
    {
        string input = """
            openai=sk-abcdefghijklmnopqrstuvwxyz123456
            gh=ghp_abcdefghijklmnopqrstuvwxyz1234567890
            github=github_pat_abcdefghijklmnopqrstuvwxyz1234567890
            google=AIzaabcdefghijklmnopqrstuvwxyz123456
            auth=Bearer abcdefghijklmnopqrstuvwxyz
            password=hunter2
            api_key="secret-value"
            access_token=token-value
            -----BEGIN PRIVATE KEY-----
            abcdef
            -----END PRIVATE KEY-----
            """;

        string result = SecretRedactor.Redact(input);

        result.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz");
        result.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz");
        result.Should().NotContain("github_pat_abcdefghijklmnopqrstuvwxyz");
        result.Should().NotContain("AIzaabcdefghijklmnopqrstuvwxyz");
        result.Should().NotContain("Bearer abcdefghijklmnopqrstuvwxyz");
        result.Should().NotContain("hunter2");
        result.Should().NotContain("secret-value");
        result.Should().NotContain("token-value");
        result.Should().NotContain("BEGIN PRIVATE KEY");
        result.Should().Contain("Bearer <redacted>");
        result.Should().Contain("password=<redacted>");
        result.Should().Contain("api_key=\"<redacted>");
        result.Should().Contain("access_token=<redacted>");
        result.Should().Contain("<redacted:private-key>");
    }

    [Fact]
    public void RedactEnvironmentFileContent_Should_RemoveEveryAssignmentValue()
    {
        string result = SecretRedactor.RedactEnvironmentFileContent(
            """
            NODE_ENV=development
            DATABASE_URL=postgres://user:pass@example/db
            export API_BASE=https://example.com
            # comment
            """);

        result.Should().Contain("NODE_ENV=<redacted>");
        result.Should().Contain("DATABASE_URL=<redacted>");
        result.Should().Contain("export API_BASE=<redacted>");
        result.Should().Contain("# comment");
        result.Should().NotContain("postgres://");
        result.Should().NotContain("development");
        result.Should().NotContain("https://example.com");
    }
}
