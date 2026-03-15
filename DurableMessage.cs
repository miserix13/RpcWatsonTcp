namespace RpcWatsonTcp
{
    /// <summary>
    /// An outbox entry persisted to Stellar.FastDB before a durable request is sent.
    /// The raw <see cref="RpcEnvelope"/> bytes are stored so the message can be
    /// replayed without re-serializing the original request.
    /// </summary>
    public sealed class DurableMessage
    {
        public Guid Id { get; set; }

        /// <summary>Pre-serialized <see cref="RpcEnvelope"/> bytes ready to be sent over the wire.</summary>
        public byte[] EnvelopeBytes { get; set; } = [];

        public DateTime CreatedAt { get; set; }
    }
}
