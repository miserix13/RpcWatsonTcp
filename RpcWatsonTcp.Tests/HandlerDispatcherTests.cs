using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

public class HandlerDispatcherTests
{
    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_HappyPath_ReturnsSerializedReply()
    {
        var handler = new EchoHandler();
        var dispatcher = new HandlerDispatcher<PingRequest, PingReply>(handler);

        byte[] requestPayload = RpcSerializer.Serialize(new PingRequest { Message = "hi" });
        (byte[] replyPayload, bool isError) = await dispatcher.DispatchAsync(requestPayload, CancellationToken.None);

        Assert.False(isError);
        PingReply reply = RpcSerializer.Deserialize<PingReply>(replyPayload);
        Assert.Equal("hi", reply.Echo);
    }

    // ── Error path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsErrorPayload()
    {
        var handler = new ThrowingHandler();
        var dispatcher = new HandlerDispatcher<PingRequest, PingReply>(handler);

        byte[] requestPayload = RpcSerializer.Serialize(new PingRequest { Message = "boom" });
        (byte[] replyPayload, bool isError) = await dispatcher.DispatchAsync(requestPayload, CancellationToken.None);

        Assert.True(isError);
        RpcErrorReply error = RpcSerializer.DeserializeErrorReply(replyPayload);
        Assert.Equal("intentional failure", error.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, error.ExceptionType);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ErrorPayloadContainsExceptionType()
    {
        var handler = new ThrowingHandler();
        var dispatcher = new HandlerDispatcher<PingRequest, PingReply>(handler);

        byte[] requestPayload = RpcSerializer.Serialize(new PingRequest());
        (byte[] replyPayload, bool isError) = await dispatcher.DispatchAsync(requestPayload, CancellationToken.None);

        Assert.True(isError);
        RpcErrorReply error = RpcSerializer.DeserializeErrorReply(replyPayload);
        Assert.NotNull(error.ExceptionType);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_CancelledToken_HandlerReceivesCancellation()
    {
        var handler = new CancellationAwareHandler();
        var dispatcher = new HandlerDispatcher<PingRequest, PingReply>(handler);

        byte[] requestPayload = RpcSerializer.Serialize(new PingRequest());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (_, bool isError) = await dispatcher.DispatchAsync(requestPayload, cts.Token);

        // Handler observed cancellation and threw — should come back as an error payload.
        Assert.True(isError);
    }

    // ── Test handlers ────────────────────────────────────────────────────────

    private sealed class EchoHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }

    private sealed class ThrowingHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("intentional failure");
    }

    private sealed class CancellationAwareHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PingReply());
        }
    }
}
