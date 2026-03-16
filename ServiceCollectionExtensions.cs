using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            Action<RpcServerOptions> configure)
        {
            var options = new RpcServerOptions();
            configure(options);

            services.AddSingleton(options);
            services.AddSingleton<DependencyInjectionHandlerRegistry>();
            services.AddSingleton<IHandlerRegistry>(sp =>
                sp.GetRequiredService<DependencyInjectionHandlerRegistry>());
            services.AddSingleton<RpcCredentialRegistry>();
            services.AddSingleton<RpcServer>();

            return services;
        }

        /// <summary>
        /// Registers an <see cref="IRpcAuthenticator{TCredential}"/> on the server for
        /// application-layer authentication. Clients must send a matching
        /// <typeparamref name="TCredential"/> via <see cref="RpcClientOptions.CredentialProvider"/>
        /// before their RPC calls will be processed.
        /// </summary>
        public static IServiceCollection AddRpcAuthentication<TCredential, TAuthenticator>(
            this IServiceCollection services)
            where TCredential : ICredential, IShapeable<TCredential>
            where TAuthenticator : class, IRpcAuthenticator<TCredential>
        {
            services.AddTransient<IRpcAuthenticator<TCredential>, TAuthenticator>();
            services.AddSingleton<ICredentialValidator>(sp =>
                new CredentialValidator<TCredential>(
                    sp.GetRequiredService<IRpcAuthenticator<TCredential>>()));
            return services;
        }

        /// <summary>
        /// Registers an <see cref="RpcClient"/> for use via dependency injection.</summary>
        public static IServiceCollection AddRpcClient(
            this IServiceCollection services,
            Action<RpcClientOptions> configure)
        {
            var options = new RpcClientOptions();
            configure(options);

            services.AddSingleton(options);
            services.AddSingleton<RpcClient>();

            return services;
        }

        /// <summary>
        /// Registers a handler for <typeparamref name="TRequest"/> → <typeparamref name="TReply"/>.
        /// Both types must be annotated with <c>[GenerateShape]</c> from PolyType.
        /// Must be called after <see cref="AddRpcServer"/>.
        /// </summary>
        public static IServiceCollection AddRpcHandler<TRequest, TReply, THandler>(
            this IServiceCollection services)
            where TRequest : IRequest, IShapeable<TRequest>
            where TReply : IReply, IShapeable<TReply>
            where THandler : class, IHandler<TRequest, TReply>
        {
            services.AddTransient<THandler>();
            services.AddTransient<IHandler<TRequest, TReply>>(sp => sp.GetRequiredService<THandler>());
            services.AddTransient<HandlerDispatcher<TRequest, TReply>>();

            // Capture the registration action to apply to the registry singleton after build.
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

        /// <summary>
        /// Registers an <see cref="RpcClient"/> together with a <see cref="DurableRpcClient"/>
        /// backed by a Stellar.FastDB outbox. Use <see cref="SendOptions.Durable"/> at the call
        /// site to opt individual requests into durable delivery.
        /// </summary>
        public static IServiceCollection AddDurableRpcClient(
            this IServiceCollection services,
            Action<RpcClientOptions> configureClient,
            Action<DurableRpcClientOptions>? configureDurable = null)
        {
            var clientOptions = new RpcClientOptions();
            configureClient(clientOptions);
            services.AddSingleton(clientOptions);
            services.AddSingleton<RpcClient>();

            var durableOptions = new DurableRpcClientOptions();
            configureDurable?.Invoke(durableOptions);
            services.AddSingleton(durableOptions);
            services.AddSingleton<DurableRpcClient>();

            return services;
        }
    }
}
