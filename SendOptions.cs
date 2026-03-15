namespace RpcWatsonTcp
{
    /// <summary>
    /// Controls per-call send behaviour. Pass <see cref="Durable"/> to opt into outbox persistence.
    /// </summary>
    public sealed record SendOptions
    {
        /// <summary>Standard send — no outbox persistence (default).</summary>
        public static readonly SendOptions Default = new();

        /// <summary>
        /// Durable send — the request is written to the FastDB outbox before transmission.
        /// If the send fails the entry stays in the outbox and will be replayed by
        /// <see cref="DurableRpcClient.DrainOutboxAsync"/>.
        /// </summary>
        public static readonly SendOptions Durable = new() { Persist = true };

        /// <summary>When true the request is persisted to the outbox before being sent.</summary>
        public bool Persist { get; init; }
    }
}
