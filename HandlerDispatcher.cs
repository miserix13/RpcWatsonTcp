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
            ITypeShapeProvider shapes,
            CancellationToken cancellationToken);
    }

    internal sealed class HandlerDispatcher<TRequest, TReply>(IHandler<TRequest, TReply> handler)
        : IHandlerDispatcher
        where TRequest : IRequest
        where TReply : IReply
    {
        public async Task<(byte[] Payload, bool IsError)> DispatchAsync(
            byte[] requestPayload,
            ITypeShapeProvider shapes,
            CancellationToken cancellationToken)
        {
            try
            {
                TRequest request = RpcSerializer.Deserialize<TRequest>(requestPayload, shapes);
                TReply reply = await handler.HandleAsync(request, cancellationToken);
                return (RpcSerializer.Serialize(reply, shapes), false);
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
