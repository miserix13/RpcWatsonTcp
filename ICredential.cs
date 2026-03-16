namespace RpcWatsonTcp
{
    /// <summary>
    /// Marker interface for application-defined credential types used in the RPC authentication handshake.
    /// Credential types must also be annotated with <c>[GenerateShape]</c> from PolyType so they can be
    /// serialized by Nerdbank.MessagePack.
    /// </summary>
    public interface ICredential { }
}
