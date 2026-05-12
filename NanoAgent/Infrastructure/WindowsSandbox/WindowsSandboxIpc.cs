using System.Buffers.Binary;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal enum WindowsSandboxIpcMessageKind
{
    SpawnRequest,
    SpawnReady,
    Output,
    Exit,
    Error,
    Terminate,
    Resize
}

internal enum WindowsSandboxOutputStream
{
    Stdout,
    Stderr
}

internal sealed class WindowsSandboxIpcMessage
{
    public WindowsSandboxIpcMessageKind Kind { get; set; }

    public WindowsSandboxOutputStream? Stream { get; set; }

    public string? PayloadBase64 { get; set; }

    public int? ExitCode { get; set; }

    public bool TimedOut { get; set; }

    public int? Columns { get; set; }

    public int? Rows { get; set; }

    public string? Error { get; set; }
}

internal static class WindowsSandboxFramedIpc
{
    public static async Task WriteAsync(
        Stream stream,
        WindowsSandboxIpcMessage message,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, WindowsSandboxJsonContext.Default.WindowsSandboxIpcMessage);
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<WindowsSandboxIpcMessage> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] prefix = await ReadExactlyAsync(stream, 4, cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException($"Invalid Windows sandbox IPC frame length '{length}'.");
        }

        byte[] payload = await ReadExactlyAsync(stream, length, cancellationToken);
        return JsonSerializer.Deserialize(payload, WindowsSandboxJsonContext.Default.WindowsSandboxIpcMessage)
               ?? throw new InvalidDataException("Invalid Windows sandbox IPC frame.");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of Windows sandbox IPC stream.");
            }

            offset += read;
        }

        return buffer;
    }
}
