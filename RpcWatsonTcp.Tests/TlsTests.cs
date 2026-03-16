using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// Integration tests for TLS support. Each test creates a self-signed certificate
/// in-memory and connects over a loopback TLS channel.
/// </summary>
public class TlsTests
{
    private static int _nextPort = 19600;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an in-memory self-signed certificate valid for 1 hour.</summary>
    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "cn=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                                              DateTimeOffset.UtcNow.AddHours(1));
        // Export and re-import with Exportable flag — required for SslStream.AuthenticateAsServer on Windows.
        var pfx = temp.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private static RpcServer BuildServer(int port, X509Certificate2 cert, bool requireAuth = false)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt =>
        {
            opt.IpAddress = "127.0.0.1";
            opt.Port = port;
            opt.Tls = new RpcServerTlsOptions { Certificate = cert };
        });
        if (requireAuth)
            services.AddRpcAuthentication<TlsApiKeyCredential, TlsApiKeyAuthenticator>();
        services.AddRpcHandler<PingRequest, PingReply, PingHandler>();
        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    private static RpcClient BuildClient(int port, bool acceptAny = true,
        ICredentialProvider? credentials = null)
        => new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            Tls = new RpcClientTlsOptions { AcceptAnyCertificate = acceptAny },
            CredentialProvider = credentials
        });

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tls_BasicRoundTrip_Works()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert);
        await using RpcClient client = BuildClient(port);

        server.Start();
        client.Connect();

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(
            new PingRequest { Message = "hello-tls" });

        Assert.Equal("hello-tls", reply.Echo);
    }

    [Fact]
    public async Task Tls_MultipleRequests_AllRepliesCorrelate()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert);
        await using RpcClient client = BuildClient(port);

        server.Start();
        client.Connect();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => client.SendAsync<PingRequest, PingReply>(
                new PingRequest { Message = $"msg-{i}" }))
            .ToList();

        PingReply[] replies = await Task.WhenAll(tasks);

        for (int i = 0; i < 10; i++)
            Assert.Equal($"msg-{i}", replies[i].Echo);
    }

    [Fact]
    public async Task Tls_InvalidServerCert_ThrowsWhenValidationEnabled()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert);

        // Client rejects self-signed cert (no AcceptAnyCertificate, no custom callback).
        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            Tls = new RpcClientTlsOptions { AcceptAnyCertificate = false }
        });

        server.Start();

        // WatsonTcp throws on Connect() or shortly after when TLS handshake fails.
        await Assert.ThrowsAnyAsync<Exception>(() =>
        {
            client.Connect();
            // Attempt an RPC to trigger the failure path.
            return client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "fail" })
                         .WaitAsync(TimeSpan.FromSeconds(3));
        });
    }

    [Fact]
    public async Task Tls_WithTls13_RoundTrip_Works()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();

        var services = new ServiceCollection();
        services.AddRpcServer(opt =>
        {
            opt.IpAddress = "127.0.0.1";
            opt.Port = port;
            opt.Tls = new RpcServerTlsOptions { Certificate = cert, TlsVersion = RpcTlsVersion.Tls13 };
        });
        services.AddRpcHandler<PingRequest, PingReply, PingHandler>();
        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();

        await using RpcServer server = sp.GetRequiredService<RpcServer>();
        server.Start();

        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            Tls = new RpcClientTlsOptions { AcceptAnyCertificate = true, TlsVersion = RpcTlsVersion.Tls13 }
        });

        try
        {
            client.Connect();
            PingReply reply = await client.SendAsync<PingRequest, PingReply>(
                new PingRequest { Message = "tls13" });
            Assert.Equal("tls13", reply.Echo);
        }
        catch (System.Security.Authentication.AuthenticationException)
        {
            // TLS 1.3 not supported on this host (requires Windows 10 1903+ / Linux / macOS) — skip.
        }
    }

    [Fact]
    public async Task Tls_CombinedWithAppLayerAuth_Works()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert, requireAuth: true);
        await using RpcClient client = BuildClient(port, acceptAny: true,
            credentials: new CredentialProvider<TlsApiKeyCredential>(
                () => new TlsApiKeyCredential { Key = "tls-valid-key" }));

        server.Start();
        await client.ConnectAsync(); // establishes TLS then authenticates

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(
            new PingRequest { Message = "tls+auth" });
        Assert.Equal("tls+auth", reply.Echo);
    }

    [Fact]
    public async Task Tls_CombinedWithAuth_WrongCredentials_Throws()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert, requireAuth: true);
        await using RpcClient client = BuildClient(port, acceptAny: true,
            credentials: new CredentialProvider<TlsApiKeyCredential>(
                () => new TlsApiKeyCredential { Key = "wrong-key" }));

        server.Start();
        await Assert.ThrowsAsync<RpcException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Tls_ClientConnected_Event_Fires()
    {
        int port = NextPort();
        using X509Certificate2 cert = CreateSelfSignedCert();
        await using RpcServer server = BuildServer(port, cert);
        server.Start();

        var tcs = new TaskCompletionSource<RpcClientConnectedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += (_, e) => tcs.TrySetResult(e);

        await using RpcClient client = BuildClient(port);
        client.Connect();

        RpcClientConnectedEventArgs args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, args.ClientGuid);
    }

    // ── Supporting types ──────────────────────────────────────────────────────

    private sealed class TlsApiKeyAuthenticator : IRpcAuthenticator<TlsApiKeyCredential>
    {
        public Task<bool> AuthenticateAsync(
            TlsApiKeyCredential credential, CancellationToken cancellationToken = default)
            => Task.FromResult(credential.Key == "tls-valid-key");
    }

    // ── Reuse handler from TestMessages ──────────────────────────────────────

    private sealed class PingHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }
}
