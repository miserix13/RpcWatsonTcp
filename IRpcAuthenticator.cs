using PolyType;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Server-side interface for validating credentials of type <typeparamref name="TCredential"/>.
    /// Register an implementation via <c>services.AddRpcAuthentication&lt;TCredential, TAuthenticator&gt;()</c>.
    /// </summary>
    /// <typeparam name="TCredential">
    /// The credential type sent by the client. Must implement <see cref="ICredential"/> and be
    /// annotated with <c>[GenerateShape]</c>.
    /// </typeparam>
    public interface IRpcAuthenticator<TCredential>
        where TCredential : ICredential, IShapeable<TCredential>
    {
        /// <summary>
        /// Validates <paramref name="credential"/> and returns <see langword="true"/> if the
        /// client is allowed to proceed, or <see langword="false"/> to reject the connection.
        /// </summary>
        Task<bool> AuthenticateAsync(TCredential credential, CancellationToken cancellationToken = default);
    }
}
