using PolyType;

namespace RpcWatsonTcp
{
    internal interface IHandlerRegistry
    {
        /// <summary>Resolves a dispatcher for the given request type, or null if no handler is registered.</summary>
        IHandlerDispatcher? Resolve(Type requestType, IServiceProvider serviceProvider);
    }

    internal sealed class DependencyInjectionHandlerRegistry : IHandlerRegistry
    {
        private readonly Dictionary<Type, Type> _dispatcherTypes = [];

        public void Register<TRequest, TReply, THandler>()
            where TRequest : IRequest
            where TReply : IReply
            where THandler : class, IHandler<TRequest, TReply>
        {
            _dispatcherTypes[typeof(TRequest)] = typeof(HandlerDispatcher<TRequest, TReply>);
        }

        public IHandlerDispatcher? Resolve(Type requestType, IServiceProvider serviceProvider)
        {
            if (!_dispatcherTypes.TryGetValue(requestType, out Type? dispatcherType))
                return null;

            return (IHandlerDispatcher)serviceProvider.GetRequiredService(dispatcherType);
        }
    }
}
