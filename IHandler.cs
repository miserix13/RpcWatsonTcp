namespace RpcWatsonTcp
{
    public interface IHandler<TRequest, TReply>
        where TReply : IReply
        where TRequest : IRequest
    {
        Task<TReply> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
    }
}
