# NanoAgent

`NanoAgent` provides the core libraries and application services behind NanoAgent, a local AI coding agent built for repository-aware development workflows.

- Repository: https://github.com/rizwan3d/NanoAgent
- Documentation: https://github.com/rizwan3d/NanoAgent/blob/master/docs/documentation.md
- Releases: https://github.com/rizwan3d/NanoAgent/releases/latest

If you want the end-user command-line experience, install the `nanoai` CLI from the release installers:

- macOS / Linux: `curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash`
- Windows PowerShell: `irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex`

See the [releases page](https://github.com/rizwan3d/NanoAgent/releases/latest) for all download options.

## Embed the SDK

The `NanoAgent.Sdk` namespace lets you drive the coding agent from your own
application (an app builder, a server, a bot, automation) without implementing
the interactive console UI. Configure a provider and model with the fluent
builder, then run turns and subscribe to progress events. Provider credentials
are held in memory only — embedding the SDK never touches the machine-wide
NanoAgent configuration.

```csharp
using NanoAgent.Sdk;

await using NanoAgentClient client = NanoAgentClient.CreateBuilder()
    .UseAnthropic(apiKey, "claude-opus-4-8")   // or UseOpenAi / UseOllama / UseOpenAiCompatible / ...
    .WithWorkspace("/path/to/repo")
    .AutoApproveTools()                          // for trusted / sandboxed automation
    .Build();

// Surface progress in your UI (final answer is returned from RunTurnAsync).
client.ToolCallsStarted += (_, e) => Console.WriteLine($"Running {e.ToolCalls.Count} tool(s)...");
client.StatusMessage   += (_, e) => Console.WriteLine($"[{e.Severity}] {e.Message}");

await client.InitializeAsync();
ConversationTurnResult result = await client.RunTurnAsync("Build a TODO app");
Console.WriteLine(result.ResponseText);
```

Extend it with your own tools and services:

```csharp
NanoAgentClient client = NanoAgentClient.CreateBuilder()
    .UseAnthropic(apiKey)
    .AddTool(new MyDeployTool())                 // custom ITool the agent can call
    .AddMcpServer(new BackendMcpServerConfiguration("docs") { Url = "https://..." })
    .ConfigureServices(services => { /* override or add any DI service */ })
    .Build();
```

The CLI and desktop apps continue to use the same core through their existing
entry points; the SDK is an additive layer on top.
