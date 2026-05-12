using System.Security.Cryptography;
using System.Security.Principal;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Reflection;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxRunnerClient
{
    private static readonly ConstructorInfo? ServerConstructorWithSecurity = typeof(NamedPipeServerStream).GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        [
            typeof(string),
            typeof(PipeDirection),
            typeof(int),
            typeof(PipeTransmissionMode),
            typeof(PipeOptions),
            typeof(int),
            typeof(int),
            typeof(System.IO.Pipes.PipeSecurity),
            typeof(System.IO.HandleInheritability),
            typeof(System.IO.Pipes.PipeAccessRights)
        ],
        modifiers: null);

    public static (string PipeIn, string PipeOut) CreatePipeNames()
    {
        Span<byte> nonce = stackalloc byte[16];
        RandomNumberGenerator.Fill(nonce);
        string text = Convert.ToHexString(nonce).ToLowerInvariant();
        return ($@"\\.\pipe\nanoagent-runner-{text}-in", $@"\\.\pipe\nanoagent-runner-{text}-out");
    }

    public static string CreateSandboxUserPipeSecurityDescriptor(string sandboxUserSid)
    {
        return $"D:(A;;GA;;;{sandboxUserSid})";
    }

    public static string CreatePipeArgument(string pipePath)
    {
        return pipePath.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
            ? pipePath[@"\\.\pipe\".Length..]
            : pipePath;
    }

    public static string ParsePipeArgument(string value)
    {
        return value.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
            ? value
            : $@"\\.\pipe\{value}";
    }

    public static NamedPipeServerStream CreateInboundServer(string pipePath, string sandboxUserSid)
    {
        return CreateServer(pipePath, PipeDirection.Out, sandboxUserSid);
    }

    public static NamedPipeServerStream CreateOutboundServer(string pipePath, string sandboxUserSid)
    {
        return CreateServer(pipePath, PipeDirection.In, sandboxUserSid);
    }

    public static NamedPipeClientStream CreateInboundClient(string pipePath)
    {
        return CreateClient(pipePath, PipeDirection.In);
    }

    public static NamedPipeClientStream CreateOutboundClient(string pipePath)
    {
        return CreateClient(pipePath, PipeDirection.Out);
    }

    private static NamedPipeServerStream CreateServer(string pipePath, PipeDirection direction, string sandboxUserSid)
    {
        System.IO.Pipes.PipeSecurity security = new();
        security.AddAccessRule(
            new System.IO.Pipes.PipeAccessRule(
                new SecurityIdentifier(sandboxUserSid),
                System.IO.Pipes.PipeAccessRights.ReadWrite | System.IO.Pipes.PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
        security.AddAccessRule(
            new System.IO.Pipes.PipeAccessRule(
                WindowsIdentity.GetCurrent().User!,
                System.IO.Pipes.PipeAccessRights.FullControl,
                AccessControlType.Allow));

        return (NamedPipeServerStream?)ServerConstructorWithSecurity?.Invoke(
                   [
                       CreatePipeArgument(pipePath),
                       direction,
                       1,
                       PipeTransmissionMode.Byte,
                       PipeOptions.Asynchronous,
                       4096,
                       4096,
                       security,
                       System.IO.HandleInheritability.None,
                       0
                   ])
               ?? throw new InvalidOperationException(
                   "Unable to create Windows sandbox named pipe server. The runtime did not expose the expected NamedPipeServerStream constructor.");
    }

    private static NamedPipeClientStream CreateClient(string pipePath, PipeDirection direction)
    {
        return new NamedPipeClientStream(
            ".",
            CreatePipeArgument(pipePath),
            direction,
            PipeOptions.Asynchronous);
    }
}
