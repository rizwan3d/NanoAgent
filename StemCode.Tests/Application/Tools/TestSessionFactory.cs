using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Tools;

internal static class TestSessionFactory
{
    public static ReplSessionContext Create(string? workspacePath = null)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            workspacePath: workspacePath);
    }
}
