using Polly;

namespace RpcWatsonTcp
{
    public sealed class RpcClientOptions
    {
        public string ServerIpAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 9000;

        /// <summary>
        /// Optional pre-shared key returned to the server during the WatsonTcp authentication
        /// handshake. Must match <see cref="RpcServerOptions.PresharedKey"/> on the server when
        /// that option is set. Leave <see langword="null"/> (the default) to skip authentication.
        /// </summary>
        /// <remarks>WatsonTcp requires the key to be exactly 16 characters.</remarks>
        public string? PresharedKey { get; set; }

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
