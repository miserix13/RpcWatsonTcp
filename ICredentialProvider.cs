using PolyType;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Client-side abstraction that supplies serialized credentials for the RPC authentication handshake.
    /// </summary>
    public interface ICredentialProvider
    {
        /// <summary>Assembly-qualified name of the credential type, used to route validation on the server.</summary>
        string CredentialTypeName { get; }

        /// <summary>Returns the MessagePack-serialized credential payload to send to the server.</summary>
        byte[] GetSerializedPayload();
    }

    /// <summary>
    /// Typed implementation of <see cref="ICredentialProvider"/> that serializes a
    /// <typeparamref name="TCredential"/> produced by <paramref name="factory"/>.
    /// </summary>
    /// <typeparam name="TCredential">
    /// The credential type. Must implement <see cref="ICredential"/> and be annotated with
    /// <c>[GenerateShape]</c> from PolyType.
    /// </typeparam>
    public sealed class CredentialProvider<TCredential> : ICredentialProvider
        where TCredential : ICredential, IShapeable<TCredential>
    {
        private readonly Func<TCredential> _factory;

        public CredentialProvider(Func<TCredential> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        /// <inheritdoc/>
        public string CredentialTypeName => typeof(TCredential).AssemblyQualifiedName!;

        /// <inheritdoc/>
        public byte[] GetSerializedPayload() => RpcSerializer.Serialize(_factory());
    }
}
