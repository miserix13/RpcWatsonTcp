using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// End-to-end tests that spin up a real RpcServer + RpcClient over loopback TCP.
/// Each test uses a unique port to avoid collisions when tests run in parallel.
/// </summary>
public class IntegrationTests
{
    private static int _nextPort = 19900;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_EchoHandler_ReturnsExpectedReply()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port);
        await using RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });

        server.Start();
        client.Connect();

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(
            new PingRequest { Message = "hello" });

        Assert.Equal("hello", reply.Echo);
    }

    [Fact]
    public async Task SendAsync_MultipleRequests_AllRepliesCorrelateCorrectly()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port);
        await using RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });

        server.Start();
        client.Connect();

        // Fire multiple requests concurrently.
        Task<PingReply>[] tasks = Enumerable.Range(0, 5)
            .Select(i => client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = $"msg{i}" }))
            .ToArray();

        PingReply[] replies = await Task.WhenAll(tasks);

        // Every reply must match its request (echo handler).
        for (int i = 0; i < 5; i++)
            Assert.Equal($"msg{i}", replies[i].Echo);
    }

    // ── Error propagation ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_HandlerThrows_ClientReceivesRpcException()
    {
        int port = NextPort();
        await using RpcServer server = BuildFaultyServer(port);
        await using RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });

        server.Start();
        client.Connect();

        RpcException ex = await Assert.ThrowsAsync<RpcException>(
            () => client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "boom" }));

        Assert.Contains("server-side failure", ex.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, ex.RemoteExceptionType);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RpcServer BuildServer(int port)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<PingRequest, PingReply, EchoHandler>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    private static RpcServer BuildFaultyServer(int port)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<PingRequest, PingReply, FaultyHandler>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    private sealed class EchoHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }

    private sealed class FaultyHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("server-side failure");
    }
}
