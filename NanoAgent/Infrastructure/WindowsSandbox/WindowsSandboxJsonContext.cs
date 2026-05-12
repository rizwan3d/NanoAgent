using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.WindowsSandbox;

[JsonSerializable(typeof(WindowsCapabilitySids))]
[JsonSerializable(typeof(WindowsSandboxSetupMarker))]
[JsonSerializable(typeof(WindowsSandboxUsersFile))]
[JsonSerializable(typeof(WindowsSandboxSetupPayload))]
[JsonSerializable(typeof(WindowsSandboxSetupError))]
[JsonSerializable(typeof(WindowsSandboxRunnerPayload))]
[JsonSerializable(typeof(WindowsSandboxRunnerResult))]
[JsonSerializable(typeof(WindowsSandboxIpcMessage))]
internal sealed partial class WindowsSandboxJsonContext : JsonSerializerContext;
