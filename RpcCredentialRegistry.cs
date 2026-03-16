using PolyType;

namespace RpcWatsonTcp
{
    internal interface ICredentialValidator
    {
        string CredentialTypeName { get; }
        Task<bool> ValidateAsync(byte[] payload, CancellationToken cancellationToken);
    }

    internal sealed class CredentialValidator<TCredential> : ICredentialValidator
        where TCredential : ICredential, IShapeable<TCredential>
    {
        private readonly IRpcAuthenticator<TCredential> _authenticator;

        public CredentialValidator(IRpcAuthenticator<TCredential> authenticator)
            => _authenticator = authenticator;

        public string CredentialTypeName => typeof(TCredential).AssemblyQualifiedName!;

        public async Task<bool> ValidateAsync(byte[] payload, CancellationToken cancellationToken)
        {
            TCredential credential = RpcSerializer.Deserialize<TCredential>(payload);
            return await _authenticator.AuthenticateAsync(credential, cancellationToken);
        }
    }

    /// <summary>
    /// Holds all registered <see cref="ICredentialValidator"/> instances and routes an incoming
    /// credential payload to the correct validator by credential type name.
    /// </summary>
    internal sealed class RpcCredentialRegistry
    {
        private readonly IReadOnlyDictionary<string, ICredentialValidator> _validators;

        public RpcCredentialRegistry(IEnumerable<ICredentialValidator> validators)
        {
            _validators = validators.ToDictionary(v => v.CredentialTypeName);
        }

        /// <summary>True when at least one credential type has been registered; the server will require authentication.</summary>
        public bool RequiresAuthentication => _validators.Count > 0;

        /// <summary>Resolves the validator for the given credential type name, or null if not registered.</summary>
        public ICredentialValidator? Resolve(string typeName) =>
            _validators.GetValueOrDefault(typeName);
    }
}
