using Polly;

namespace RpcWatsonTcp
{
    public sealed class RpcClientOptions
    {
        public string ServerIpAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 9000;

        /// <summary>
        /// Optional provider that supplies serialized credentials for the application-layer
        /// authentication handshake. When set, <see cref="RpcClient.ConnectAsync"/> sends the
        /// credentials immediately after establishing the TCP connection and awaits the server's
        /// response before any RPC calls proceed.
        /// <para>
        /// Use <see cref="CredentialProvider{TCredential}"/> to create a typed instance:
        /// <code>
        /// opt.CredentialProvider = new CredentialProvider&lt;ApiKeyCredential&gt;(
        ///     () => new ApiKeyCredential { Key = "my-key" });
        /// </code>
        /// </para>
        /// Leave <see langword="null"/> (the default) to skip authentication.
        /// </summary>
        public ICredentialProvider? CredentialProvider { get; set; }

        /// <summary>
        /// Optional Polly v8 resilience pipeline applied to every <c>SendAsync</c> call.
        /// Configure retry, circuit breaker, timeout, or any combination:
        /// <code>
        /// opt.ResiliencePipeline = new ResiliencePipelineBuilder()
        ///     .AddTimeout(TimeSpan.FromSeconds(5))
        ///     .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
        ///     .AddCircuitBreaker(new CircuitBreakerStrategyOptions { ... })
        ///     .Build();
        /// </code>
        /// Leave null to use no resilience pipeline (default).
        /// </summary>
        public ResiliencePipeline? ResiliencePipeline { get; set; }
    }
}
