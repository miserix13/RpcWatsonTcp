namespace RpcWatsonTcp
{
    public sealed class RpcServerOptions
    {
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;

        /// <summary>
        /// Optional pre-shared key that clients must present during the WatsonTcp handshake.
        /// When set, connections from clients that do not supply the same key are rejected and
        /// the server raises <see cref="RpcServer.AuthenticationFailed"/>.
        /// Leave <see langword="null"/> (the default) to allow unauthenticated connections.
        /// </summary>
        /// <remarks>WatsonTcp requires the key to be exactly 16 characters.</remarks>
        public string? PresharedKey { get; set; }
    }
}
