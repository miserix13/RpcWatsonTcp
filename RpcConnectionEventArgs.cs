namespace RpcWatsonTcp
{
    /// <summary>Raised when a remote client establishes a connection to the <see cref="RpcServer"/>.</summary>
    public sealed class RpcClientConnectedEventArgs : EventArgs
    {
        public RpcClientConnectedEventArgs(Guid clientGuid, string ipPort)
        {
            ClientGuid = clientGuid;
            IpPort = ipPort;
        }

        /// <summary>WatsonTcp-assigned unique identifier for this client connection.</summary>
        public Guid ClientGuid { get; }

        /// <summary>IP address and port of the connected client, e.g. <c>127.0.0.1:54321</c>.</summary>
        public string IpPort { get; }
    }

    /// <summary>Raised when a remote client disconnects from the <see cref="RpcServer"/>.</summary>
    public sealed class RpcClientDisconnectedEventArgs : EventArgs
    {
        public RpcClientDisconnectedEventArgs(Guid clientGuid, string ipPort, string reason)
        {
            ClientGuid = clientGuid;
            IpPort = ipPort;
            Reason = reason;
        }

        /// <summary>WatsonTcp-assigned unique identifier for the disconnected client.</summary>
        public Guid ClientGuid { get; }

        /// <summary>IP address and port of the disconnected client.</summary>
        public string IpPort { get; }

        /// <summary>
        /// Human-readable disconnect reason, e.g. <c>Normal</c>, <c>Timeout</c>, or <c>AuthFailure</c>.
        /// </summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Raised on the <see cref="RpcServer"/> when a client successfully authenticates using the
    /// configured <see cref="RpcServerOptions.PresharedKey"/>.
    /// </summary>
    public sealed class RpcAuthenticationSucceededEventArgs : EventArgs
    {
        public RpcAuthenticationSucceededEventArgs(Guid clientGuid, string ipPort)
        {
            ClientGuid = clientGuid;
            IpPort = ipPort;
        }

        /// <summary>WatsonTcp-assigned unique identifier for the authenticated client.</summary>
        public Guid ClientGuid { get; }

        /// <summary>IP address and port of the authenticated client.</summary>
        public string IpPort { get; }
    }

    /// <summary>
    /// Raised on the <see cref="RpcServer"/> when a client fails authentication because it supplied
    /// an incorrect or missing <see cref="RpcServerOptions.PresharedKey"/>.
    /// </summary>
    public sealed class RpcAuthenticationFailedEventArgs : EventArgs
    {
        public RpcAuthenticationFailedEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        /// <summary>IP address and port of the client that failed authentication.</summary>
        public string IpPort { get; }
    }
}
