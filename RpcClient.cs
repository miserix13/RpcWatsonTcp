using System.Collections.Concurrent;
using Polly;
using PolyType;
using WatsonTcp;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Sends RPC requests to an <see cref="RpcServer"/> and awaits typed replies.
    /// </summary>
    public sealed class RpcClient : IAsyncDisposable
    {
        private readonly WatsonTcpClient _tcpClient;
        private readonly ResiliencePipeline? _pipeline;
        private readonly ICredentialProvider? _credentialProvider;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RpcEnvelope>> _pending = new();
        private readonly TaskCompletionSource<bool>? _authTcs;
        private readonly Task _authReady;
        private int _disposed;

        /// <summary>Raised when the server confirms the client's credentials are valid.</summary>
        public event EventHandler? AuthenticationSucceeded;

        /// <summary>Raised when the server rejects the client's credentials.</summary>
        public event EventHandler? AuthenticationFailed;

        public RpcClient(RpcClientOptions options)
        {
            _pipeline = options.ResiliencePipeline;
            _credentialProvider = options.CredentialProvider;
            _tcpClient = new WatsonTcpClient(options.ServerIpAddress, options.ServerPort);
            _tcpClient.Events.MessageReceived += OnMessageReceived;
            _tcpClient.Events.ServerConnected += OnServerConnected;

            if (_credentialProvider is not null)
            {
                _authTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _authReady = _authTcs.Task;
            }
            else
            {
                _authReady = Task.CompletedTask;
            }
        }

        /// <summary>
        /// Establishes the TCP connection. When <see cref="RpcClientOptions.CredentialProvider"/>
        /// is configured, sends credentials immediately but does not wait for the server's reply.
        /// Use <see cref="ConnectAsync"/> to await authentication completion before proceeding.
        /// </summary>
        public void Connect() => _tcpClient.Connect();

        /// <summary>
        /// Establishes the TCP connection and, when <see cref="RpcClientOptions.CredentialProvider"/>
        /// is configured, waits for the server to accept or reject credentials before returning.
        /// Throws <see cref="RpcException"/> if authentication fails.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _tcpClient.Connect();
            await _authReady.WaitAsync(cancellationToken);
        }

        private void OnServerConnected(object? sender, ConnectionEventArgs e)
        {
            if (_credentialProvider is null) return;
            _ = SendAuthEnvelopeAsync();
        }

        private async Task SendAuthEnvelopeAsync()
        {
            try
            {
                var envelope = new RpcEnvelope
                {
                    MessageId = Guid.NewGuid(),
                    TypeName = _credentialProvider!.CredentialTypeName,
                    Payload = _credentialProvider.GetSerializedPayload(),
                    IsAuth = true
                };
                await _tcpClient.SendAsync(RpcSerializer.SerializeEnvelope(envelope));
            }
            catch (Exception ex)
            {
                _authTcs?.TrySetException(ex);
            }
        }

        /// <summary>
        /// Sends <paramref name="request"/> to the server and returns the typed reply.
        /// If <see cref="RpcClientOptions.CredentialProvider"/> is configured, waits for the
        /// authentication handshake to complete first.
        /// If <see cref="RpcClientOptions.ResiliencePipeline"/> is configured, the call is
        /// wrapped in that pipeline (retry / circuit breaker / timeout).
        /// </summary>
        /// <exception cref="RpcException">Thrown when the server handler raises an exception or when authentication fails.</exception>
        public Task<TReply> SendAsync<TRequest, TReply>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest, IShapeable<TRequest>
            where TReply : IReply, IShapeable<TReply>
        {
            if (_pipeline is not null)
                return _pipeline.ExecuteAsync(
                    ct => new ValueTask<TReply>(SendCoreAsync<TRequest, TReply>(request, ct)),
                    cancellationToken).AsTask();

            return SendCoreAsync<TRequest, TReply>(request, cancellationToken);
        }

        private async Task<TReply> SendCoreAsync<TRequest, TReply>(
            TRequest request,
            CancellationToken cancellationToken)
            where TRequest : IRequest, IShapeable<TRequest>
            where TReply : IReply, IShapeable<TReply>
        {
            // Wait for authentication to complete (no-op when no credentials are configured).
            await _authReady.WaitAsync(cancellationToken);

            var messageId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<RpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[messageId] = tcs;

            try
            {
                cancellationToken.Register(() =>
                {
                    if (_pending.TryRemove(messageId, out _))
                        tcs.TrySetCanceled(cancellationToken);
                });

                var envelope = new RpcEnvelope
                {
                    MessageId = messageId,
                    TypeName = typeof(TRequest).AssemblyQualifiedName!,
                    Payload = RpcSerializer.Serialize(request),
                    IsError = false
                };

                await _tcpClient.SendAsync(RpcSerializer.SerializeEnvelope(envelope));

                RpcEnvelope reply = await tcs.Task.WaitAsync(cancellationToken);

                if (reply.IsError)
                {
                    RpcErrorReply error = RpcSerializer.DeserializeErrorReply(reply.Payload);
                    throw new RpcException(error);
                }

                return RpcSerializer.Deserialize<TReply>(reply.Payload);
            }
            finally
            {
                _pending.TryRemove(messageId, out _);
            }
        }

        /// <summary>
        /// Sends pre-built envelope bytes directly over TCP without registering a reply TCS.
        /// Used by <see cref="DurableRpcClient.DrainOutboxAsync"/> to replay persisted messages
        /// without needing the original generic type parameters.
        /// </summary>
        internal Task SendRawAsync(byte[] envelopeBytes) =>
            _tcpClient.SendAsync(envelopeBytes);

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            RpcEnvelope envelope;
            try
            {
                envelope = RpcSerializer.DeserializeEnvelope(e.Data);
            }
            catch
            {
                return; // Malformed reply — discard.
            }

            if (envelope.IsAuth)
            {
                HandleAuthReply(envelope);
                return;
            }

            if (_pending.TryRemove(envelope.MessageId, out TaskCompletionSource<RpcEnvelope>? tcs))
                tcs.TrySetResult(envelope);
            // No TCS found → reply was for a fire-and-forget drain replay; discard silently.
        }

        private void HandleAuthReply(RpcEnvelope envelope)
        {
            if (envelope.IsError)
            {
                var ex = new RpcException(new RpcErrorReply
                {
                    Message = "Authentication failed.",
                    ExceptionType = typeof(RpcException).FullName
                });
                _authTcs?.TrySetException(ex);
                AuthenticationFailed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _authTcs?.TrySetResult(true);
                AuthenticationSucceeded?.Invoke(this, EventArgs.Empty);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            _tcpClient.Events.MessageReceived -= OnMessageReceived;
            _tcpClient.Events.ServerConnected -= OnServerConnected;

            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();

            await Task.Run(() => _tcpClient.Dispose());
        }
    }
}
