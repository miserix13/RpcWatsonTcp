using Polly;

namespace RpcWatsonTcp
{
    public sealed class RpcClientOptions
    {
        public string ServerIpAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 9000;

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
