using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RpcEnvelope>> _pending = new();

        public RpcClient(RpcClientOptions options)
        {
            _tcpClient = new WatsonTcpClient(options.ServerIpAddress, options.ServerPort);
            _tcpClient.Events.MessageReceived += OnMessageReceived;
        }

        public void Connect() => _tcpClient.Connect();

        /// <summary>
        /// Sends <paramref name="request"/> to the server and returns the typed reply.
        /// <typeparamref name="TRequest"/> and <typeparamref name="TReply"/> must be annotated
        /// with <c>[GenerateShape]</c> from PolyType so Nerdbank.MessagePack can serialize them.
        /// </summary>
        /// <exception cref="RpcException">Thrown when the server handler raises an exception.</exception>
        public async Task<TReply> SendAsync<TRequest, TReply>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest, IShapeable<TRequest>
            where TReply : IReply, IShapeable<TReply>
        {
            var messageId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<RpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[messageId] = tcs;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.Token.Register(() =>
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
        }

        public async ValueTask DisposeAsync()
        {
            _tcpClient.Events.MessageReceived -= OnMessageReceived;

            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();

            await Task.Run(() => _tcpClient.Dispose());
        }
    }
}
