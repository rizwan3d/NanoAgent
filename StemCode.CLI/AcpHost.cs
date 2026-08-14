namespace StemCode.CLI;

internal static class AcpHost
{
    public static async Task<int> RunAsync(
        string[] args,
        string? providerAuthKey,
        bool noOldReader,
        bool autoApproveAllTools)
    {
        AcpServer server = new(
            Console.In,
            Console.Out,
            Console.Error,
            args,
            providerAuthKey,
            noOldReader,
            autoApproveAllTools);

        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;

        try
        {
            await server.RunAsync(cancellation.Token);
            return ExitCodeMapper.Success;
        }
        catch (OperationCanceledException)
        {
            return ExitCodeMapper.Cancelled;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"StemCode ACP error: {exception.Message}");
            return ExitCodeMapper.Error;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
            await server.DisposeAsync();
        }
    }
}
