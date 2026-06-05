using FluentAssertions;
using NanoAgent.Infrastructure.Mcp;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Mcp;

public sealed class McpHttpServerClientTests
{
    [Fact]
    public async Task ListToolsAsync_ShouldProcessFirstSseEventWithoutWaitingForStreamClosure()
    {
        StreamingSseHandler handler = new(
            "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}\n\n");
        using HttpClient httpClient = new(handler);
        McpServerConfiguration configuration = new("test-server")
        {
            Url = "https://mcp.example.com"
        };
        await using McpHttpServerClient sut = new(
            httpClient,
            configuration,
            TimeSpan.FromMilliseconds(200));

        IReadOnlyList<McpRemoteTool> tools = await sut.ListToolsAsync(CancellationToken.None);

        tools.Should().BeEmpty();
        handler.AcceptHeaderValues.Should().Contain("application/json");
        handler.AcceptHeaderValues.Should().Contain("text/event-stream");
    }

    [Fact]
    public async Task CallToolAsync_ShouldThrowWhenSseResponseContainsNoDataEvent()
    {
        StreamingSseHandler handler = new(
            "event: message\n\n",
            leaveOpenAfterPayload: false);
        using HttpClient httpClient = new(handler);
        McpServerConfiguration configuration = new("test-server")
        {
            Url = "https://mcp.example.com"
        };
        await using McpHttpServerClient sut = new(
            httpClient,
            configuration,
            TimeSpan.FromSeconds(1));
        using JsonDocument arguments = JsonDocument.Parse("{}");

        Func<Task> act = async () => await sut.CallToolAsync("demo", arguments.RootElement, CancellationToken.None);

        await act.Should().ThrowAsync<McpProtocolException>()
            .WithMessage("The MCP server returned an empty SSE response.");
    }

    private sealed class StreamingSseHandler : HttpMessageHandler
    {
        private readonly string _payload;
        private readonly bool _leaveOpenAfterPayload;

        public StreamingSseHandler(string payload, bool leaveOpenAfterPayload = true)
        {
            _payload = payload;
            _leaveOpenAfterPayload = leaveOpenAfterPayload;
        }

        public IReadOnlyList<string> AcceptHeaderValues { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AcceptHeaderValues = request.Headers.Accept.Select(static header => header.MediaType ?? string.Empty).ToArray();

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(new HangingAfterPrefixStream(
                    Encoding.UTF8.GetBytes(_payload),
                    _leaveOpenAfterPayload))
            };
            response.Content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class HangingAfterPrefixStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly bool _leaveOpenAfterPrefix;
        private int _position;

        public HangingAfterPrefixStream(byte[] prefix, bool leaveOpenAfterPrefix)
        {
            _prefix = prefix;
            _leaveOpenAfterPrefix = leaveOpenAfterPrefix;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                int bytesToCopy = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, bytesToCopy).CopyTo(buffer);
                _position += bytesToCopy;
                return ValueTask.FromResult(bytesToCopy);
            }

            if (!_leaveOpenAfterPrefix)
            {
                return ValueTask.FromResult(0);
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private static async ValueTask<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
