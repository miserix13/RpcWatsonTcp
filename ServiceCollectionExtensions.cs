using Microsoft.Extensions.DependencyInjection;
using PolyType;

namespace RpcWatsonTcp
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers an <see cref="RpcServer"/> and its supporting services.
        /// Call <see cref="AddRpcHandler{TRequest,TReply,THandler}"/> after this to register handlers.
        /// </summary>
        public static IServiceCollection AddRpcServer(
            this IServiceCollection services,
            Action<RpcServerOptions> configure,
            ITypeShapeProvider shapes)
        {
            var options = new RpcServerOptions();
            configure(options);

            services.AddSingleton(options);
            services.AddSingleton<DependencyInjectionHandlerRegistry>();
            services.AddSingleton<IHandlerRegistry>(sp =>
                sp.GetRequiredService<DependencyInjectionHandlerRegistry>());
            services.AddSingleton(shapes);
            services.AddSingleton<RpcServer>();

            return services;
        }

        /// <summary>
        /// Registers an <see cref="RpcClient"/> for use via dependency injection.
        /// </summary>
        public static IServiceCollection AddRpcClient(
            this IServiceCollection services,
            Action<RpcClientOptions> configure,
            ITypeShapeProvider shapes)
        {
            var options = new RpcClientOptions();
            configure(options);

            services.AddSingleton(options);
            services.AddSingleton(shapes);
            services.AddSingleton<RpcClient>();

            return services;
        }

        /// <summary>
        /// Registers a handler for <typeparamref name="TRequest"/> → <typeparamref name="TReply"/>.
        /// Must be called after <see cref="AddRpcServer"/>.
        /// </summary>
        public static IServiceCollection AddRpcHandler<TRequest, TReply, THandler>(
            this IServiceCollection services)
            where TRequest : IRequest
            where TReply : IReply
            where THandler : class, IHandler<TRequest, TReply>
        {
            services.AddTransient<THandler>();
            services.AddTransient<IHandler<TRequest, TReply>>(sp => sp.GetRequiredService<THandler>());
            services.AddTransient<HandlerDispatcher<TRequest, TReply>>();

            // Register the request→dispatcher mapping on the registry singleton.
            services.AddSingleton<Action<DependencyInjectionHandlerRegistry>>(r =>
                r.Register<TRequest, TReply, THandler>());

            return services;
        }

        /// <summary>
        /// Applies all handler registrations to the <see cref="DependencyInjectionHandlerRegistry"/>.
        /// Call this once after building the <see cref="IServiceProvider"/>.
        /// </summary>
        public static void ApplyRpcHandlerRegistrations(this IServiceProvider serviceProvider)
        {
            var registry = serviceProvider.GetRequiredService<DependencyInjectionHandlerRegistry>();
            var registrations = serviceProvider.GetServices<Action<DependencyInjectionHandlerRegistry>>();
            foreach (var apply in registrations)
                apply(registry);
        }
    }
}
