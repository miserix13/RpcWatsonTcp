using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Benchmarks;

/// <summary>
/// Measures single-request round-trip latency and serialization overhead.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class RpcRoundTripBenchmark : IAsyncDisposable
{
    private RpcServer _server = null!;
    private RpcClient _client = null!;
    private static int _port = 20100;

    [GlobalSetup]
    public void Setup()
    {
        int port = System.Threading.Interlocked.Increment(ref _port);

        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();

        _server = sp.GetRequiredService<RpcServer>();
        _server.Start();

        _client = new RpcClient(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port
        });
        _client.Connect();

        // Warm up
        _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" }).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Benchmark(Description = "Round-trip: small payload (16 B)")]
    public Task<BenchReply> SmallPayload() =>
        _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "0123456789abcdef" });

    [Benchmark(Description = "Round-trip: medium payload (256 B)")]
    public Task<BenchReply> MediumPayload() =>
        _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = new string('x', 256) });

    [Benchmark(Description = "Round-trip: large payload (4 KB)")]
    public Task<BenchReply> LargePayload() =>
        _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = new string('x', 4096) });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class EchoHandler : IHandler<BenchRequest, BenchReply>
    {
        public Task<BenchReply> HandleAsync(BenchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BenchReply { Echo = request.Payload });
    }
}

/// <summary>
/// Measures throughput when N requests are sent concurrently.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ConcurrentThroughputBenchmark
{
    private RpcServer _server = null!;
    private RpcClient _client = null!;
    private static int _port = 20200;

    [Params(1, 10, 50)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int port = System.Threading.Interlocked.Increment(ref _port);

        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();

        _server = sp.GetRequiredService<RpcServer>();
        _server.Start();

        _client = new RpcClient(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port
        });
        _client.Connect();

        // Warm up
        _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" }).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Benchmark(Description = "Concurrent round-trips")]
    public Task ConcurrentRequests()
    {
        var tasks = new Task[Concurrency];
        for (int i = 0; i < Concurrency; i++)
            tasks[i] = _client.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = $"msg{i}" });
        return Task.WhenAll(tasks);
    }

    private sealed class EchoHandler : IHandler<BenchRequest, BenchReply>
    {
        public Task<BenchReply> HandleAsync(BenchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BenchReply { Echo = request.Payload });
    }
}

/// <summary>
/// Measures the overhead of the application-layer authentication handshake:
/// ConnectAsync latency (one-time per connection) and per-request round-trip
/// when authentication is required vs. not required.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class AuthenticatedRoundTripBenchmark
{
    private RpcServer _server = null!;
    private RpcClient _clientNoAuth = null!;
    private RpcClient _clientWithAuth = null!;
    private static int _port = 20300;

    [GlobalSetup]
    public async Task Setup()
    {
        int port = System.Threading.Interlocked.Increment(ref _port);
        int portAuth = System.Threading.Interlocked.Increment(ref _port);

        // Server without auth
        var svcNoAuth = new ServiceCollection();
        svcNoAuth.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        svcNoAuth.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();
        IServiceProvider spNoAuth = svcNoAuth.BuildServiceProvider();
        spNoAuth.ApplyRpcHandlerRegistrations();
        spNoAuth.GetRequiredService<RpcServer>().Start();

        // Server with auth
        var svcAuth = new ServiceCollection();
        svcAuth.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = portAuth; });
        svcAuth.AddRpcAuthentication<BenchCredential, TokenAuthenticator>();
        svcAuth.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();
        IServiceProvider spAuth = svcAuth.BuildServiceProvider();
        spAuth.ApplyRpcHandlerRegistrations();
        _server = spAuth.GetRequiredService<RpcServer>();
        _server.Start();

        _clientNoAuth = new RpcClient(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = port });
        _clientNoAuth.Connect();

        _clientWithAuth = new RpcClient(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = portAuth,
            CredentialProvider = new CredentialProvider<BenchCredential>(
                () => new BenchCredential { Token = "bench-token" })
        });
        await _clientWithAuth.ConnectAsync(); // await auth handshake before benchmarking RPCs

        // Warm up both
        await _clientNoAuth.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" });
        await _clientWithAuth.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" });
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _clientNoAuth.DisposeAsync();
        await _clientWithAuth.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Benchmark(Description = "Round-trip: no auth")]
    public Task<BenchReply> NoAuth() =>
        _clientNoAuth.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "hello" });

    [Benchmark(Description = "Round-trip: with auth (post-handshake)")]
    public Task<BenchReply> WithAuth() =>
        _clientWithAuth.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "hello" });

    private sealed class EchoHandler : IHandler<BenchRequest, BenchReply>
    {
        public Task<BenchReply> HandleAsync(BenchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BenchReply { Echo = request.Payload });
    }

    private sealed class TokenAuthenticator : IRpcAuthenticator<BenchCredential>
    {
        public Task<bool> AuthenticateAsync(BenchCredential credential, CancellationToken cancellationToken = default)
            => Task.FromResult(credential.Token == "bench-token");
    }
}

/// <summary>
/// Measures the per-request overhead introduced by TLS 1.2 encryption on an already-established
/// connection, compared to a plain TCP baseline.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class TlsRoundTripBenchmark
{
    private RpcServer _serverNoTls = null!;
    private RpcServer _serverTls = null!;
    private RpcClient _clientNoTls = null!;
    private RpcClient _clientTls = null!;
    private X509Certificate2 _cert = null!;
    private static int _port = 20400;

    [GlobalSetup]
    public void Setup()
    {
        int portNoTls = System.Threading.Interlocked.Increment(ref _port);
        int portTls   = System.Threading.Interlocked.Increment(ref _port);

        // Self-signed cert — export+re-import with Exportable flag for SslStream on Windows.
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "cn=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temp = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var pfx = temp.Export(X509ContentType.Pfx);
        _cert = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);

        // Plain TCP server
        var svcNoTls = new ServiceCollection();
        svcNoTls.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = portNoTls; });
        svcNoTls.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();
        IServiceProvider spNoTls = svcNoTls.BuildServiceProvider();
        spNoTls.ApplyRpcHandlerRegistrations();
        _serverNoTls = spNoTls.GetRequiredService<RpcServer>();
        _serverNoTls.Start();

        // TLS 1.2 server
        var svcTls = new ServiceCollection();
        svcTls.AddRpcServer(opt =>
        {
            opt.IpAddress = "127.0.0.1";
            opt.Port = portTls;
            opt.Tls = new RpcServerTlsOptions { Certificate = _cert };
        });
        svcTls.AddRpcHandler<BenchRequest, BenchReply, EchoHandler>();
        IServiceProvider spTls = svcTls.BuildServiceProvider();
        spTls.ApplyRpcHandlerRegistrations();
        _serverTls = spTls.GetRequiredService<RpcServer>();
        _serverTls.Start();

        _clientNoTls = new RpcClient(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = portNoTls
        });
        _clientNoTls.Connect();

        _clientTls = new RpcClient(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = portTls,
            Tls = new RpcClientTlsOptions { AcceptAnyCertificate = true }
        });
        _clientTls.Connect();

        // Warm up both connections
        _clientNoTls.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" }).GetAwaiter().GetResult();
        _clientTls.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "warm" }).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _clientNoTls.DisposeAsync();
        await _clientTls.DisposeAsync();
        await _serverNoTls.DisposeAsync();
        await _serverTls.DisposeAsync();
        _cert.Dispose();
    }

    [Benchmark(Description = "Round-trip: plain TCP (baseline)")]
    public Task<BenchReply> PlainTcp() =>
        _clientNoTls.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "hello" });

    [Benchmark(Description = "Round-trip: TLS 1.2")]
    public Task<BenchReply> WithTls() =>
        _clientTls.SendAsync<BenchRequest, BenchReply>(new BenchRequest { Payload = "hello" });

    private sealed class EchoHandler : IHandler<BenchRequest, BenchReply>
    {
        public Task<BenchReply> HandleAsync(BenchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BenchReply { Echo = request.Payload });
    }
}

/// <summary>
/// Measures pure serialization/deserialization overhead with no network.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class SerializerBenchmark
{
    private readonly BenchRequest _smallRequest = new() { Payload = "0123456789abcdef" };
    private readonly BenchRequest _largeRequest = new() { Payload = new string('x', 4096) };
    private readonly RpcEnvelope _envelope = new()
    {
        MessageId = Guid.NewGuid(),
        TypeName = typeof(BenchRequest).AssemblyQualifiedName!,
        Payload = [],
        IsError = false
    };

    private byte[] _smallBytes = [];
    private byte[] _largeBytes = [];
    private byte[] _envelopeBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _smallBytes = RpcSerializer.Serialize(_smallRequest);
        _largeBytes = RpcSerializer.Serialize(_largeRequest);
        _envelopeBytes = RpcSerializer.SerializeEnvelope(_envelope);
    }

    [Benchmark(Description = "Serialize: small request")]
    public byte[] SerializeSmall() => RpcSerializer.Serialize(_smallRequest);

    [Benchmark(Description = "Serialize: large request (4 KB)")]
    public byte[] SerializeLarge() => RpcSerializer.Serialize(_largeRequest);

    [Benchmark(Description = "Serialize: RpcEnvelope")]
    public byte[] SerializeEnvelope() => RpcSerializer.SerializeEnvelope(_envelope);

    [Benchmark(Description = "Deserialize: small request")]
    public BenchRequest DeserializeSmall() => RpcSerializer.Deserialize<BenchRequest>(_smallBytes);

    [Benchmark(Description = "Deserialize: large request (4 KB)")]
    public BenchRequest DeserializeLarge() => RpcSerializer.Deserialize<BenchRequest>(_largeBytes);

    [Benchmark(Description = "Deserialize: RpcEnvelope")]
    public RpcEnvelope DeserializeEnvelope() => RpcSerializer.DeserializeEnvelope(_envelopeBytes);
}
