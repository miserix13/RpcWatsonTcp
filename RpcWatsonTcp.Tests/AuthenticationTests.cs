using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// Tests for the authentication extensibility points: PresharedKey option and the
/// ClientConnected / ClientDisconnected / AuthenticationSucceeded / AuthenticationFailed events.
/// </summary>
public class AuthenticationTests
{
    private static int _nextPort = 19700;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    // ── Connection lifecycle events ──────────────────────────────────────────

    [Fact]
    public async Task ClientConnected_Event_FiresWhenClientConnects()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, presharedKey: null);
        server.Start();

        var tcs = new TaskCompletionSource<RpcClientConnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += (_, e) => tcs.TrySetResult(e);

        await using RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });
        client.Connect();

        RpcClientConnectedEventArgs args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(args.IpPort));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
    }

    [Fact]
    public async Task ClientDisconnected_Event_FiresWhenClientDisconnects()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, presharedKey: null);
        server.Start();

        var connected = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<RpcClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.ClientConnected += (_, e) => connected.TrySetResult(e.ClientGuid);
        server.ClientDisconnected += (_, e) => disconnected.TrySetResult(e);

        RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });
        client.Connect();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposing the client closes the TCP connection.
        await client.DisposeAsync();

        RpcClientDisconnectedEventArgs args = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
        Assert.False(string.IsNullOrEmpty(args.Reason));
    }

    // ── Preshared key — happy path ───────────────────────────────────────────

    [Fact]
    public async Task PresharedKey_MatchingKey_ClientCanSendRpc()
    {
        int port = NextPort();
        const string key = "super-secret-key"; // exactly 16 chars

        await using RpcServer server = BuildServer(port, presharedKey: key);
        server.Start();

        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            PresharedKey = key
        });

        // Wait for the async auth handshake to complete before sending RPCs.
        var authReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AuthenticationSucceeded += (_, _) => authReady.TrySetResult(true);
        client.Connect();
        await authReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "auth-test" });
        Assert.Equal("auth-test", reply.Echo);
    }

    [Fact]
    public async Task PresharedKey_MatchingKey_ServerRaisesAuthenticationSucceeded()
    {
        int port = NextPort();
        const string key = "serverauthkey123"; // exactly 16 chars

        await using RpcServer server = BuildServer(port, presharedKey: key);
        server.Start();

        var authOk = new TaskCompletionSource<RpcAuthenticationSucceededEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.AuthenticationSucceeded += (_, e) => authOk.TrySetResult(e);

        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            PresharedKey = key
        });
        client.Connect();

        RpcAuthenticationSucceededEventArgs args = await authOk.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(args.IpPort));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
    }

    [Fact]
    public async Task PresharedKey_MatchingKey_ClientRaisesAuthenticationSucceeded()
    {
        int port = NextPort();
        const string key = "clientauthkey123"; // exactly 16 chars

        await using RpcServer server = BuildServer(port, presharedKey: key);
        server.Start();

        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            PresharedKey = key
        });

        var authOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AuthenticationSucceeded += (_, _) => authOk.TrySetResult(true);

        client.Connect();
        bool succeeded = await authOk.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(succeeded);
    }

    // ── Preshared key — failure path ─────────────────────────────────────────

    [Fact]
    public async Task PresharedKey_WrongKey_ServerRaisesAuthenticationFailed()
    {
        int port = NextPort();

        await using RpcServer server = BuildServer(port, presharedKey: "correctpassword!"); // 16 chars
        server.Start();

        var authFailed = new TaskCompletionSource<RpcAuthenticationFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.AuthenticationFailed += (_, e) => authFailed.TrySetResult(e);

        // Client intentionally provides a wrong key (must still be 16 chars).
        RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            PresharedKey = "wrongpassword123" // 16 chars, wrong value
        });
        client.Connect();

        RpcAuthenticationFailedEventArgs args = await authFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(args.IpPort));

        await client.DisposeAsync();
    }

    [Fact]
    public async Task PresharedKey_WrongKey_ClientRaisesAuthenticationFailed()
    {
        int port = NextPort();

        await using RpcServer server = BuildServer(port, presharedKey: "correctpassword!"); // 16 chars
        server.Start();

        RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            PresharedKey = "wrongpassword123" // 16 chars, wrong value
        });

        var authFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AuthenticationFailed += (_, _) => authFailed.TrySetResult(true);

        client.Connect();
        bool fired = await authFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fired);

        await client.DisposeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RpcServer BuildServer(int port, string? presharedKey)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt =>
        {
            opt.IpAddress = "127.0.0.1";
            opt.Port = port;
            opt.PresharedKey = presharedKey;
        });
        services.AddRpcHandler<PingRequest, PingReply, EchoHandler>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    private sealed class EchoHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }
}
