namespace RpcWatsonTcp
{
    /// <summary>
    /// Thrown on the client when the server returns an <see cref="RpcErrorReply"/>.
    /// </summary>
    public class RpcException : Exception
    {
        public string? RemoteExceptionType { get; }

        public RpcException(RpcErrorReply errorReply)
            : base(errorReply.Message)
        {
            RemoteExceptionType = errorReply.ExceptionType;
        }
    }
}
