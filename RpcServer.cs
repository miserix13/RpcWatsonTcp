using Microsoft.Extensions.DependencyInjection;
using PolyType;
using WatsonTcp;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Hosts RPC handlers over a TCP connection using WatsonTcp.
    /// Incoming requests are deserialized, dispatched to the registered handler,
    /// and the reply is sent back to the originating client.
    /// </summary>
    public sealed class RpcServer : IAsyncDisposable
    {
        private readonly WatsonTcpServer _tcpServer;
        private readonly IHandlerRegistry _registry;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>Raised when a client establishes a TCP connection (after authentication, if a preshared key is configured).</summary>
        public event EventHandler<RpcClientConnectedEventArgs>? ClientConnected;

        /// <summary>Raised when a client disconnects. <see cref="RpcClientDisconnectedEventArgs.Reason"/> is <c>AuthFailure</c> when the client failed authentication.</summary>
        public event EventHandler<RpcClientDisconnectedEventArgs>? ClientDisconnected;

        /// <summary>Raised when a client successfully authenticates using <see cref="RpcServerOptions.PresharedKey"/>.</summary>
        public event EventHandler<RpcAuthenticationSucceededEventArgs>? AuthenticationSucceeded;

        /// <summary>Raised when a client fails authentication because it supplied an incorrect or missing preshared key.</summary>
        public event EventHandler<RpcAuthenticationFailedEventArgs>? AuthenticationFailed;

        public RpcServer(RpcServerOptions options, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = serviceProvider.GetRequiredService<IHandlerRegistry>();
            _tcpServer = new WatsonTcpServer(options.IpAddress, options.Port);

            if (options.PresharedKey is not null)
            {
                if (options.PresharedKey.Length != 16)
                    throw new ArgumentException("PresharedKey must be exactly 16 characters.", nameof(options));
                _tcpServer.Settings.PresharedKey = options.PresharedKey;
            }

            _tcpServer.Events.MessageReceived += OnMessageReceived;
            _tcpServer.Events.ClientConnected += OnClientConnected;
            _tcpServer.Events.ClientDisconnected += OnClientDisconnected;
            _tcpServer.Events.AuthenticationSucceeded += OnAuthenticationSucceeded;
            _tcpServer.Events.AuthenticationFailed += OnAuthenticationFailed;
        }

        public void Start() => _tcpServer.Start();

        private void OnClientConnected(object? sender, ConnectionEventArgs e) =>
            ClientConnected?.Invoke(this, new RpcClientConnectedEventArgs(e.Client.Guid, e.Client.IpPort));

        private void OnClientDisconnected(object? sender, DisconnectionEventArgs e) =>
            ClientDisconnected?.Invoke(this, new RpcClientDisconnectedEventArgs(e.Client.Guid, e.Client.IpPort, e.Reason.ToString()));

        private void OnAuthenticationSucceeded(object? sender, AuthenticationSucceededEventArgs e) =>
            AuthenticationSucceeded?.Invoke(this, new RpcAuthenticationSucceededEventArgs(e.Client.Guid, e.Client.IpPort));

        private void OnAuthenticationFailed(object? sender, AuthenticationFailedEventArgs e) =>
            AuthenticationFailed?.Invoke(this, new RpcAuthenticationFailedEventArgs(e.IpPort));

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            // Fire-and-forget per message; errors are caught inside DispatchAndReplyAsync.
            _ = DispatchAndReplyAsync(e.Client.Guid, e.Data);
        }

        private async Task DispatchAndReplyAsync(Guid clientGuid, byte[] data)
        {
            RpcEnvelope requestEnvelope;
            try
            {
                requestEnvelope = RpcSerializer.DeserializeEnvelope(data);
            }
            catch
            {
                // Malformed envelope — cannot reply without a MessageId.
                return;
            }

            byte[] replyPayload;
            bool isError;

            try
            {
                Type? requestType = Type.GetType(requestEnvelope.TypeName);
                if (requestType is null)
                    throw new InvalidOperationException($"Unknown request type: '{requestEnvelope.TypeName}'.");

                IHandlerDispatcher? dispatcher = _registry.Resolve(requestType, _serviceProvider);
                if (dispatcher is null)
                    throw new InvalidOperationException($"No handler registered for '{requestEnvelope.TypeName}'.");

                (replyPayload, isError) = await dispatcher.DispatchAsync(
                    requestEnvelope.Payload, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var error = new RpcErrorReply { Message = ex.Message, ExceptionType = ex.GetType().FullName };
                replyPayload = RpcSerializer.SerializeErrorReply(error);
                isError = true;
            }

            var replyEnvelope = new RpcEnvelope
            {
                MessageId = requestEnvelope.MessageId,
                TypeName = isError ? typeof(RpcErrorReply).AssemblyQualifiedName! : string.Empty,
                Payload = replyPayload,
                IsError = isError
            };

            byte[] replyBytes = RpcSerializer.SerializeEnvelope(replyEnvelope);
            await _tcpServer.SendAsync(clientGuid, replyBytes);
        }

        public async ValueTask DisposeAsync()
        {
            _tcpServer.Events.MessageReceived -= OnMessageReceived;
            _tcpServer.Events.ClientConnected -= OnClientConnected;
            _tcpServer.Events.ClientDisconnected -= OnClientDisconnected;
            _tcpServer.Events.AuthenticationSucceeded -= OnAuthenticationSucceeded;
            _tcpServer.Events.AuthenticationFailed -= OnAuthenticationFailed;
            await Task.Run(() => _tcpServer.Dispose());
        }
    }
}
