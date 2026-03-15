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
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RpcEnvelope>> _pending = new();
        private int _disposed;

        public RpcClient(RpcClientOptions options)
        {
            _pipeline = options.ResiliencePipeline;
            _tcpClient = new WatsonTcpClient(options.ServerIpAddress, options.ServerPort);
            _tcpClient.Events.MessageReceived += OnMessageReceived;
        }

        public void Connect() => _tcpClient.Connect();

        /// <summary>
        /// Sends <paramref name="request"/> to the server and returns the typed reply.
        /// If <see cref="RpcClientOptions.ResiliencePipeline"/> is configured, the call is
        /// wrapped in that pipeline (retry / circuit breaker / timeout).
        /// <typeparamref name="TRequest"/> and <typeparamref name="TReply"/> must be annotated
        /// with <c>[GenerateShape]</c> from PolyType so Nerdbank.MessagePack can serialize them.
        /// </summary>
        /// <exception cref="RpcException">Thrown when the server handler raises an exception.</exception>
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

            if (_pending.TryRemove(envelope.MessageId, out TaskCompletionSource<RpcEnvelope>? tcs))
                tcs.TrySetResult(envelope);
            // No TCS found → reply was for a fire-and-forget drain replay; discard silently.
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            _tcpClient.Events.MessageReceived -= OnMessageReceived;

            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();

            await Task.Run(() => _tcpClient.Dispose());
        }
    }
}
