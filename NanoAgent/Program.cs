namespace NanoAgent;

internal static class Program
{
    public static Task<int> Main(string[] args) => Hosting.NanoAgentHostBootstrap.RunAsync(args);
}
