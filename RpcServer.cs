using System.Collections.Concurrent;
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
        private readonly RpcCredentialRegistry _credentials;

        // Tracks per-client authentication state. True = authenticated (or no auth required).
        private readonly ConcurrentDictionary<Guid, bool> _clientAuth = new();

        /// <summary>Raised when a client establishes a TCP connection.</summary>
        public event EventHandler<RpcClientConnectedEventArgs>? ClientConnected;

        /// <summary>Raised when a client disconnects.</summary>
        public event EventHandler<RpcClientDisconnectedEventArgs>? ClientDisconnected;

        /// <summary>Raised when a client successfully completes the application-layer authentication handshake.</summary>
        public event EventHandler<RpcAuthenticationSucceededEventArgs>? AuthenticationSucceeded;

        /// <summary>Raised when a client fails the application-layer authentication handshake.</summary>
        public event EventHandler<RpcAuthenticationFailedEventArgs>? AuthenticationFailed;

        public RpcServer(RpcServerOptions options, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = serviceProvider.GetRequiredService<IHandlerRegistry>();
            _credentials = serviceProvider.GetRequiredService<RpcCredentialRegistry>();
            _tcpServer = options.Tls is { } tls
                ? BuildTlsServer(options.IpAddress, options.Port, tls)
                : new WatsonTcpServer(options.IpAddress, options.Port);
            _tcpServer.Events.MessageReceived += OnMessageReceived;
            _tcpServer.Events.ClientConnected += OnClientConnected;
            _tcpServer.Events.ClientDisconnected += OnClientDisconnected;
        }

        private static WatsonTcpServer BuildTlsServer(string ip, int port, RpcServerTlsOptions tls)
        {
            WatsonTcp.TlsVersion tlsVer = tls.TlsVersion == RpcTlsVersion.Tls12
                ? WatsonTcp.TlsVersion.Tls12
                : WatsonTcp.TlsVersion.Tls13;

            WatsonTcpServer server;
            if (tls.Certificate is not null)
                server = new WatsonTcpServer(ip, port, tls.Certificate, tlsVer);
            else if (tls.PfxPath is not null)
                server = new WatsonTcpServer(ip, port, tls.PfxPath, tls.PfxPassword ?? string.Empty, tlsVer);
            else
                throw new ArgumentException(
                    $"{nameof(RpcServerTlsOptions)}.{nameof(RpcServerTlsOptions.Certificate)} or " +
                    $"{nameof(RpcServerTlsOptions.PfxPath)} must be set when TLS is enabled.", nameof(tls));

            server.SslConfiguration.ClientCertificateRequired = tls.RequireClientCertificate;
            if (tls.ClientCertificateValidation is not null)
                server.SslConfiguration.ClientCertificateValidationCallback = tls.ClientCertificateValidation;

            return server;
        }

        public void Start() => _tcpServer.Start();

        private void OnClientConnected(object? sender, ConnectionEventArgs e)
        {
            // Mark as authenticated immediately when no auth is configured.
            _clientAuth[e.Client.Guid] = !_credentials.RequiresAuthentication;
            ClientConnected?.Invoke(this, new RpcClientConnectedEventArgs(e.Client.Guid, e.Client.IpPort));
        }

        private void OnClientDisconnected(object? sender, DisconnectionEventArgs e)
        {
            _clientAuth.TryRemove(e.Client.Guid, out _);
            ClientDisconnected?.Invoke(this, new RpcClientDisconnectedEventArgs(
                e.Client.Guid, e.Client.IpPort, e.Reason.ToString()));
        }

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            _ = DispatchAndReplyAsync(e.Client.Guid, e.Client.IpPort, e.Data);
        }

        private async Task DispatchAndReplyAsync(Guid clientGuid, string ipPort, byte[] data)
        {
            RpcEnvelope requestEnvelope;
            try
            {
                requestEnvelope = RpcSerializer.DeserializeEnvelope(data);
            }
            catch
            {
                return; // Malformed envelope — cannot reply without a MessageId.
            }

            if (requestEnvelope.IsAuth)
            {
                await HandleAuthEnvelopeAsync(clientGuid, ipPort, requestEnvelope);
                return;
            }

            // Reject unauthenticated RPC calls when auth is required.
            if (!_clientAuth.GetValueOrDefault(clientGuid, false))
            {
                var errorReply = new RpcErrorReply
                {
                    Message = "Authentication required. Send credentials before issuing RPC calls.",
                    ExceptionType = typeof(RpcException).FullName
                };
                var rejectEnvelope = new RpcEnvelope
                {
                    MessageId = requestEnvelope.MessageId,
                    TypeName = typeof(RpcErrorReply).AssemblyQualifiedName!,
                    Payload = RpcSerializer.SerializeErrorReply(errorReply),
                    IsError = true
                };
                await _tcpServer.SendAsync(clientGuid, RpcSerializer.SerializeEnvelope(rejectEnvelope));
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

            await _tcpServer.SendAsync(clientGuid, RpcSerializer.SerializeEnvelope(replyEnvelope));
        }

        private async Task HandleAuthEnvelopeAsync(Guid clientGuid, string ipPort, RpcEnvelope authEnvelope)
        {
            bool success = false;
            try
            {
                ICredentialValidator? validator = _credentials.Resolve(authEnvelope.TypeName);
                if (validator is not null)
                    success = await validator.ValidateAsync(authEnvelope.Payload, CancellationToken.None);
            }
            catch { }

            _clientAuth[clientGuid] = success;

            var reply = new RpcEnvelope
            {
                MessageId = authEnvelope.MessageId,
                IsAuth = true,
                IsError = !success
            };
            await _tcpServer.SendAsync(clientGuid, RpcSerializer.SerializeEnvelope(reply));

            if (success)
                AuthenticationSucceeded?.Invoke(this,
                    new RpcAuthenticationSucceededEventArgs(clientGuid, ipPort));
            else
                AuthenticationFailed?.Invoke(this,
                    new RpcAuthenticationFailedEventArgs(ipPort));
        }

        public async ValueTask DisposeAsync()
        {
            _tcpServer.Events.MessageReceived -= OnMessageReceived;
            _tcpServer.Events.ClientConnected -= OnClientConnected;
            _tcpServer.Events.ClientDisconnected -= OnClientDisconnected;
            try
            {
                await Task.Run(() => _tcpServer.Dispose());
            }
            catch (Exception ex) when (IsCancellationFromWatsonTcp(ex)) { }
        }

        // WatsonTcp TLS cleanup internally calls Task.Wait() on canceled tasks, surfacing either a
        // TaskCanceledException or an AggregateException wrapping one. Suppress those silently.
        private static bool IsCancellationFromWatsonTcp(Exception ex) =>
            ex is OperationCanceledException ||
            (ex is AggregateException agg &&
             agg.InnerExceptions.All(e => e is OperationCanceledException));
    }
}
