using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

public class HandlerRegistryTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<PingHandler>();
        services.AddTransient<IHandler<PingRequest, PingReply>>(sp => sp.GetRequiredService<PingHandler>());
        services.AddTransient<HandlerDispatcher<PingRequest, PingReply>>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Register_ThenResolve_ReturnsDispatcher()
    {
        var registry = new DependencyInjectionHandlerRegistry();
        registry.Register<PingRequest, PingReply, PingHandler>();

        IHandlerDispatcher? dispatcher = registry.Resolve(typeof(PingRequest), BuildProvider());

        Assert.NotNull(dispatcher);
        Assert.IsType<HandlerDispatcher<PingRequest, PingReply>>(dispatcher);
    }

    [Fact]
    public void Resolve_UnknownType_ReturnsNull()
    {
        var registry = new DependencyInjectionHandlerRegistry();
        IHandlerDispatcher? dispatcher = registry.Resolve(typeof(PingRequest), BuildProvider());
        Assert.Null(dispatcher);
    }

    [Fact]
    public void Register_OverwritesSameRequestType()
    {
        var registry = new DependencyInjectionHandlerRegistry();
        registry.Register<PingRequest, PingReply, PingHandler>();
        registry.Register<PingRequest, PingReply, PingHandler>(); // second registration same type

        // Should still resolve without error.
        Assert.NotNull(registry.Resolve(typeof(PingRequest), BuildProvider()));
    }

    // ── Minimal handler for testing ──────────────────────────────────────────

    private sealed class PingHandler : IHandler<PingRequest, PingReply>
    {
        public Task<PingReply> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingReply { Echo = request.Message });
    }
}
