using PolyType;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Non-generic dispatcher interface allowing the server to invoke a typed handler
    /// without knowing <typeparamref name="TRequest"/> / <typeparamref name="TReply"/> at call-site.
    /// </summary>
    internal interface IHandlerDispatcher
    {
        Task<(byte[] Payload, bool IsError)> DispatchAsync(
            byte[] requestPayload,
            CancellationToken cancellationToken);
    }

    internal sealed class HandlerDispatcher<TRequest, TReply>(IHandler<TRequest, TReply> handler)
        : IHandlerDispatcher
        where TRequest : IRequest, IShapeable<TRequest>
        where TReply : IReply, IShapeable<TReply>
    {
        public async Task<(byte[] Payload, bool IsError)> DispatchAsync(
            byte[] requestPayload,
            CancellationToken cancellationToken)
        {
            try
            {
                TRequest request = RpcSerializer.Deserialize<TRequest>(requestPayload);
                TReply reply = await handler.HandleAsync(request, cancellationToken);
                return (RpcSerializer.Serialize(reply), false);
            }
            catch (Exception ex)
            {
                var error = new RpcErrorReply
                {
                    Message = ex.Message,
                    ExceptionType = ex.GetType().FullName
                };
                return (RpcSerializer.SerializeErrorReply(error), true);
            }
        }
    }
}
