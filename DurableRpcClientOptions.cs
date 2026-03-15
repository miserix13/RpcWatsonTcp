namespace RpcWatsonTcp
{
    public sealed class DurableRpcClientOptions
    {
        /// <summary>Path to the Stellar.FastDB outbox file (created if absent).</summary>
        public string OutboxPath { get; set; } = "rpc_outbox.fastdb";

        /// <summary>
        /// When true, <see cref="DurableRpcClient"/> automatically calls
        /// <see cref="DurableRpcClient.DrainOutboxAsync"/> after <see cref="DurableRpcClient.Connect"/>.
        /// </summary>
        public bool DrainOnConnect { get; set; } = true;
    }
}
