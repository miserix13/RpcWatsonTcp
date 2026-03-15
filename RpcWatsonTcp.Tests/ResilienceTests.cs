using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// Verifies that a <see cref="Polly.ResiliencePipeline"/> wired into
/// <see cref="RpcClientOptions.ResiliencePipeline"/> retries failed requests
/// and ultimately succeeds (or propagates after exhausting attempts).
/// </summary>
public class ResilienceTests
{
    private static int _nextPort = 19950;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    // ── Retry succeeds after transient failures ──────────────────────────────

    [Fact]
    public async Task RetryPipeline_TransientServerFailures_EventuallySucceeds()
    {
        int port = NextPort();

        // Handler fails the first 2 calls, then succeeds.
        var handler = new CountingFaultyHandler(failCount: 2);
        await using RpcServer server = BuildServer(port, handler);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(10),
                ShouldHandle = new PredicateBuilder().Handle<RpcException>()
            })
            .Build();

        var clientOptions = new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            ResiliencePipeline = pipeline
        };

        server.Start();
        await using RpcClient client = new(clientOptions);
        client.Connect();

        PingReply reply = await client.SendAsync<PingRequest, PingReply>(
            new PingRequest { Message = "retry-test" });

        Assert.Equal("retry-test", reply.Echo);
        Assert.Equal(3, handler.CallCount); // 2 failures + 1 success
    }

    [Fact]
    public async Task RetryPipeline_ExhaustsAttempts_ThrowsRpcException()
    {
        int port = NextPort();

        // Handler always fails.
        var handler = new CountingFaultyHandler(failCount: 99);
        await using RpcServer server = BuildServer(port, handler);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(10),
                ShouldHandle = new PredicateBuilder().Handle<RpcException>()
            })
            .Build();

        var clientOptions = new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port,
            ResiliencePipeline = pipeline
        };

        server.Start();
        await using RpcClient client = new(clientOptions);
        client.Connect();

        await Assert.ThrowsAsync<RpcException>(
            () => client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "fail" }));

        // 1 original + 2 retries = 3 total calls
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task NoPipeline_ServerFailure_ThrowsRpcExceptionImmediately()
    {
        int port = NextPort();

        var handler = new CountingFaultyHandler(failCount: 99);
        await using RpcServer server = BuildServer(port, handler);

        server.Start();
        await using RpcClient client = new(new RpcClientOptions
        {
            ServerIpAddress = "127.0.0.1",
            ServerPort = port
            // No ResiliencePipeline
        });
        client.Connect();

        await Assert.ThrowsAsync<RpcException>(
            () => client.SendAsync<PingRequest, PingReply>(new PingRequest { Message = "no-retry" }));

        Assert.Equal(1, handler.CallCount); // exactly one attempt
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RpcServer BuildServer(int port, CountingFaultyHandler handler)
    {
        var services = new ServiceCollection();
        services.AddRpcServer(opt => { opt.IpAddress = "127.0.0.1"; opt.Port = port; });
        services.AddRpcHandler<PingRequest, PingReply, CountingFaultyHandler>();
        // Register the specific instance AFTER AddRpcHandler so it wins over the transient.
        services.AddSingleton(handler);

        IServiceProvider sp = services.BuildServiceProvider();
        sp.ApplyRpcHandlerRegistrations();
        return sp.GetRequiredService<RpcServer>();
    }

    /// <summary>
    /// Fails the first <see cref="failCount"/> calls, then echoes the request.
    /// </summary>
    private sealed class CountingFaultyHandler(int failCount) : IHandler<PingRequest, PingReply>
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
        {
            int count = Interlocked.Increment(ref _callCount);
            if (count <= failCount)
                throw new InvalidOperationException($"Simulated failure #{count}");

            return Task.FromResult(new PingReply { Echo = request.Message });
        }
    }
}
