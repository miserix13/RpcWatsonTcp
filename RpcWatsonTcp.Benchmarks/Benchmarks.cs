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
