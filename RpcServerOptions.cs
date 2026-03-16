namespace RpcWatsonTcp
{
    public sealed class RpcServerOptions
    {
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;

        /// <summary>
        /// Optional TLS configuration. When set, the server listens over an encrypted TLS connection.
        /// A server certificate is required; leave <see langword="null"/> for plain TCP (default).
        /// </summary>
        public RpcServerTlsOptions? Tls { get; set; }
    }
}
