using FluentAssertions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class SecretRedactorTests
{
    private const string R = "<redacted>";
    private const string Rpk = "<redacted:private-key>";

    [Fact]
    public void Redact_Should_RedactOpenAiApiKeys()
    {
        string key = "sk-" + "abcdefghijklmnopqrstuvwxyz";
        string result = SecretRedactor.Redact("api key is " + key);
        result.Should().NotContain(key);
        result.Should().Contain(R);
    }

    [Fact]
    public void Redact_Should_RedactGitHubTokens()
    {
        string key = "ghp_" + "abcdefghijklmnopqrstuvwxyz123456";
        string result = SecretRedactor.Redact("token=" + key);
        result.Should().NotContain(key);
        result.Should().Contain(R);
    }

    [Fact]
    public void Redact_Should_RedactGitHubPatTokens()
    {
        string key = "github_pat_" + "abcdefghijklmnopqrstuvwxyz123456";
        string result = SecretRedactor.Redact("pat=" + key);
        result.Should().NotContain(key);
        result.Should().Contain(R);
    }

    [Fact]
    public void Redact_Should_RedactGoogleApiKeys()
    {
        string key = "AIza" + "abcdefghijklmnopqrstuvwxyz123456";
        string result = SecretRedactor.Redact("key=" + key);
        result.Should().NotContain(key);
        result.Should().Contain(R);
    }

    [Fact]
    public void Redact_Should_RedactBearerTokens()
    {
        string prefix = "Autho" + "rization: ";
        string bPrefix = "Bea" + "rer ";
        string jwt = "eyJ" + "hbGciOiJIUzI1NiJ9." + "dGVzdA." + "dGhlIHRva2Vu";
        string input = prefix + bPrefix + jwt;
        string result = SecretRedactor.Redact(input);
        result.Should().NotContain(jwt);
        result.Should().Contain(R);
    }

    [Fact]
    public void Redact_Should_RedactPrivateKeyBlocks()
    {
        string block = "-----BEGIN RSA PRIVATE KEY-----\n" +
            "MIIEpAIBAAKCAQEA1c8F3MDFmQ4p\n" +
            "-----END RSA PRIVATE KEY-----";
        string result = SecretRedactor.Redact(block);
        result.Should().NotContain("BEGIN RSA PRIVATE KEY");
        result.Should().Contain(Rpk);
    }

    [Fact]
    public void Redact_Should_RedactEcPrivateKeyBlocks()
    {
        string block = "-----BEGIN EC PRIVATE KEY-----\n" +
            "MHQCAQEEIIm3V2o=\n" +
            "-----END EC PRIVATE KEY-----";
        string result = SecretRedactor.Redact(block);
        result.Should().NotContain("BEGIN EC PRIVATE KEY");
        result.Should().Contain(Rpk);
    }

    [Fact]
    public void Redact_Should_RedactSensitiveAssignmentsByKeyName()
    {
        string v1 = "wJalrXUtnFEMI";
        string v2 = "8a2b3c4d5e6f7g";
        string v3 = "hunter2";
        string v4 = "fa1c3e5b7d9f0e";
        string v5 = "s3cr3t";
        string v6 = "t0k3n";

        string input = "api_key = " + v1 + "\n" +
            "ACCESS_TOKEN = " + v2 + "\n" +
            "my_password = " + v3 + "\n" +
            "client_secret = " + v4 + "\n" +
            "secret_key = " + v5 + "\n" +
            "my_secret_token = " + v6;

        string result = SecretRedactor.Redact(input);

        result.Should().NotContain(v1);
        result.Should().NotContain(v2);
        result.Should().NotContain(v3);
        result.Should().NotContain(v4);
        result.Should().NotContain(v5);
        result.Should().NotContain(v6);
        result.Should().Contain("api_key = " + R);
        result.Should().Contain("ACCESS_TOKEN = " + R);
        result.Should().Contain("my_password = " + R);
        result.Should().Contain("client_secret = " + R);
        result.Should().Contain("secret_key = " + R);
        result.Should().Contain("my_secret_token = " + R);
    }

    [Fact]
    public void Redact_Should_NotRedactNonSecretCodePatterns()
    {
        string input = "int maxRetries = 3;\n" +
            "string nodeEnv = \"development\";\n" +
            "var databaseUrl = \"postgres://localhost/mydb\";\n" +
            "const string contentType = \"application/json\";\n" +
            "string secretMessage = \"hello world\";\n" +
            "int tokenCount = 5;\n" +
            "string version = \"1.0.0\";\n" +
            "string language = \"en-US\";\n" +
            "int retryCount = 5;\n" +
            "string commitMessage = \"fix: update token handling\";";

        string result = SecretRedactor.Redact(input);

        result.Should().Contain("int maxRetries = 3");
        result.Should().Contain("string nodeEnv = \"development\"");
        result.Should().Contain("var databaseUrl = \"postgres://localhost/mydb\"");
        result.Should().Contain("const string contentType = \"application/json\"");
        result.Should().Contain("string secretMessage = \"hello world\"");
        result.Should().Contain("int tokenCount = 5");
        result.Should().Contain("string version = \"1.0.0\"");
        result.Should().Contain("string language = \"en-US\"");
        result.Should().Contain("int retryCount = 5");
        result.Should().Contain("string commitMessage = \"fix: update token handling\"");
    }

    [Fact]
    public void Redact_Should_NotRedactGenericEnvVarAssignments()
    {
        string input = "NODE_ENV=development\n" +
            "VERSION=1.0.0\n" +
            "MAX_RETRIES=3\n" +
            "LANGUAGE=en-US";

        string result = SecretRedactor.Redact(input);

        result.Should().Contain("NODE_ENV=development");
        result.Should().Contain("VERSION=1.0.0");
        result.Should().Contain("MAX_RETRIES=3");
        result.Should().Contain("LANGUAGE=en-US");
    }

    [Fact]
    public void Redact_Should_HandleNullAndEmpty()
    {
        SecretRedactor.Redact(null).Should().BeEmpty();
        SecretRedactor.Redact("").Should().BeEmpty();
        SecretRedactor.Redact("   ").Should().Be("   ");
    }

    [Fact]
    public void RedactEnvironmentFileContent_Should_RemoveEveryAssignmentValue()
    {
        string input = "NODE_ENV=development\n" +
            "DATABASE_URL=postgres://user:pass@example/db\n" +
            "export API_BASE=https://example.com\n" +
            "# comment";

        string result = SecretRedactor.RedactEnvironmentFileContent(input);

        result.Should().Contain("NODE_ENV=" + R);
        result.Should().Contain("DATABASE_URL=" + R);
        result.Should().Contain("export API_BASE=" + R);
        result.Should().Contain("# comment");
        result.Should().NotContain("postgres://");
        result.Should().NotContain("development");
        result.Should().NotContain("https://example.com");
    }

    [Fact]
    public void IsEnvironmentFilePath_Should_DetectEnvFiles()
    {
        SecretRedactor.IsEnvironmentFilePath(".env").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("/project/.env").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("C:\\project\\.env").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath(".env.production").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("config/.env.local").Should().BeTrue();
        SecretRedactor.IsEnvironmentFilePath("appsettings.json").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath("").Should().BeFalse();
        SecretRedactor.IsEnvironmentFilePath(null).Should().BeFalse();
    }
}
