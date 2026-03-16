using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// Tests for the generic application-layer authentication extensibility points:
/// ICredential / IRpcAuthenticator / ICredentialProvider, the
/// ClientConnected / ClientDisconnected / AuthenticationSucceeded / AuthenticationFailed events,
/// and the _authReady gate inside RpcClient.SendAsync.
/// </summary>
public class AuthenticationTests
{
    private static int _nextPort = 19700;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    const string ValidKey = "valid-api-key";
    const string WrongKey = "wrong-api-key";

    // ── Connection lifecycle events (no auth configured) ─────────────────────

    [Fact]
    public async Task ClientConnected_Event_FiresWhenClientConnects()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: false);
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
        await using RpcServer server = BuildServer(port, requireAuth: false);
        server.Start();

        var connected = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<RpcClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.ClientConnected += (_, e) => connected.TrySetResult(e.ClientGuid);
        server.ClientDisconnected += (_, e) => disconnected.TrySetResult(e);

        RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });
        client.Connect();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.DisposeAsync();

        RpcClientDisconnectedEventArgs args = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
        Assert.False(string.IsNullOrEmpty(args.Reason));
    }

    // ── No auth configured — passthrough ─────────────────────────────────────

    [Fact]
    public async Task NoAuth_Client_CanSendRpcWithoutCredentials()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: false);
        server.Start();

        await using RpcClient client = new(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });
        client.Connect();

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "hi" });
        Assert.Equal("hi", reply.Echo);
    }

    // ── Valid credentials — happy path ────────────────────────────────────────

    [Fact]
    public async Task ValidCredential_ServerRaisesAuthenticationSucceeded()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        var authOk = new TaskCompletionSource<RpcAuthenticationSucceededEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.AuthenticationSucceeded += (_, e) => authOk.TrySetResult(e);

        await using RpcClient client = BuildClient(port, apiKey: ValidKey);
        client.Connect();

        RpcAuthenticationSucceededEventArgs args = await authOk.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(args.IpPort));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
    }

    [Fact]
    public async Task ValidCredential_ClientRaisesAuthenticationSucceeded()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        await using RpcClient client = BuildClient(port, apiKey: ValidKey);

        var authOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AuthenticationSucceeded += (_, _) => authOk.TrySetResult(true);
        client.Connect();

        Assert.True(await authOk.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ValidCredential_ConnectAsync_ThenRpcSucceeds()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        await using RpcClient client = BuildClient(port, apiKey: ValidKey);
        await client.ConnectAsync();

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "authenticated" });
        Assert.Equal("authenticated", reply.Echo);
    }

    [Fact]
    public async Task ValidCredential_SendAsync_AutomaticallyAwaitsAuthBeforeSending()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        await using RpcClient client = BuildClient(port, apiKey: ValidKey);

        // Call Connect() (not ConnectAsync) — SendAsync must still gate on auth internally.
        client.Connect();
        PingReply reply = await client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "gated" });
        Assert.Equal("gated", reply.Echo);
    }

    // ── Invalid credentials — failure path ────────────────────────────────────

    [Fact]
    public async Task InvalidCredential_ServerRaisesAuthenticationFailed()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        var authFailed = new TaskCompletionSource<RpcAuthenticationFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.AuthenticationFailed += (_, e) => authFailed.TrySetResult(e);

        RpcClient client = BuildClient(port, apiKey: WrongKey);
        client.Connect();

        RpcAuthenticationFailedEventArgs args = await authFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(args.IpPort));

        await client.DisposeAsync();
    }

    [Fact]
    public async Task InvalidCredential_ClientRaisesAuthenticationFailed()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        RpcClient client = BuildClient(port, apiKey: WrongKey);

        var authFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AuthenticationFailed += (_, _) => authFailed.TrySetResult(true);
        client.Connect();

        Assert.True(await authFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task InvalidCredential_ConnectAsync_Throws()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        await using RpcClient client = BuildClient(port, apiKey: WrongKey);
        await Assert.ThrowsAsync<RpcException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task InvalidCredential_SendAsync_Throws()
    {
        int port = NextPort();
        await using RpcServer server = BuildServer(port, requireAuth: true);
        server.Start();

        // Connect() without awaiting — SendAsync must propagate the auth failure.
        await using RpcClient client = BuildClient(port, apiKey: WrongKey);
        client.Connect();

        await Assert.ThrowsAsync<RpcException>(() =>
            client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "should fail" }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RpcServer BuildServer(int port, bool requireAuth)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<PingRequest, PingReply, EchoHandler>();

        if (requireAuth)
            services.AddRpcAuthentication<ApiKeyCredential, ApiKeyAuthenticator>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    private static RpcClient BuildClient(int port, string apiKey) =>
        new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            CredentialProvider = new CredentialProvider<ApiKeyCredential>(
                () => new ApiKeyCredential { ApiKey = apiKey })
        });

    private sealed class EchoHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }

    private sealed class ApiKeyAuthenticator : IRpcAuthenticator<ApiKeyCredential>
    {
        public Task<bool> AuthenticateAsync(ApiKeyCredential credential, CancellationToken cancellationToken = default)
            => Task.FromResult(credential.ApiKey == ValidKey);
    }
}
