using PolyType;
using Stellar.Collections;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Wraps <see cref="RpcClient"/> and adds an opt-in outbox for durable message delivery.
    /// Pass <see cref="SendOptions.Durable"/> to persist a request to Stellar.FastDB before
    /// sending it; the entry is removed only after a successful reply is received.
    /// On startup, call <see cref="DrainOutboxAsync"/> (or set <see cref="DurableRpcClientOptions.DrainOnConnect"/>)
    /// to replay any messages that survived a previous crash or disconnect.
    /// </summary>
    /// <remarks>
    /// Delivery semantics are <b>at-least-once</b>: replayed messages may be processed more than
    /// once if the server received the original request before the client crashed.
    /// Handlers should be idempotent where possible.
    /// </remarks>
    public sealed class DurableRpcClient : IAsyncDisposable
    {
        private readonly RpcClient _inner;
        private readonly FastDB _db;
        private readonly IFastDBCollection<Guid, DurableMessage> _outbox;
        private readonly bool _drainOnConnect;

        public DurableRpcClient(RpcClient inner, DurableRpcClientOptions options)
        {
            _inner = inner;
            _drainOnConnect = options.DrainOnConnect;

            var fullPath = Path.GetFullPath(options.OutboxPath);
            var dir = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);

            _db = new FastDB("rpc_outbox", new FastDBOptions { BaseDirectory = dir });
            _outbox = _db.GetCollection<Guid, DurableMessage>();
        }

        /// <summary>
        /// Connects the underlying <see cref="RpcClient"/> and, if
        /// <see cref="DurableRpcClientOptions.DrainOnConnect"/> is true, drains any pending outbox entries.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _inner.ConnectAsync(cancellationToken);

            if (_drainOnConnect)
                await DrainOutboxAsync(cancellationToken);
        }

        /// <summary>
        /// Sends a request to the server.
        /// When <paramref name="options"/> is <see cref="SendOptions.Durable"/> the request is
        /// written to the FastDB outbox <em>before</em> transmission and removed only on a
        /// successful reply. On failure the entry stays in the outbox for later replay.
        /// </summary>
        /// <exception cref="RpcException">Thrown when the server handler raises an exception.</exception>
        public async Task<TReply> SendAsync<TRequest, TReply>(
            TRequest request,
            SendOptions? options = null,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest, IShapeable<TRequest>
            where TReply : IReply, IShapeable<TReply>
        {
            if (options?.Persist != true)
                return await _inner.SendAsync<TRequest, TReply>(request, cancellationToken);

            // ── Durable path ─────────────────────────────────────────────────
            var messageId = Guid.NewGuid();
            var entry = new DurableMessage
            {
                Id = messageId,
                EnvelopeBytes = RpcSerializer.SerializeEnvelope(new RpcEnvelope
                {
                    MessageId = messageId,
                    TypeName = typeof(TRequest).AssemblyQualifiedName!,
                    Payload = RpcSerializer.Serialize(request),
                    IsError = false
                }),
                CreatedAt = DateTime.UtcNow
            };

            await _outbox.AddOrUpdateAsync(entry.Id, entry);

            try
            {
                TReply reply = await _inner.SendAsync<TRequest, TReply>(request, cancellationToken);
                await _outbox.RemoveAsync(entry.Id);
                return reply;
            }
            catch
            {
                // Leave entry in outbox for DrainOutboxAsync to replay later.
                throw;
            }
        }

        /// <summary>
        /// Replays all pending outbox entries by sending their raw envelope bytes over TCP
        /// (fire-and-forget — replies are discarded). Successfully sent entries are removed
        /// from the outbox; failed entries remain for the next drain.
        /// </summary>
        public async Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        {
            var pending = _outbox.Cast<DurableMessage>().ToList();

            foreach (DurableMessage entry in pending)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await _inner.SendRawAsync(entry.EnvelopeBytes);
                    await _outbox.RemoveAsync(entry.Id);
                }
                catch
                {
                    // Leave in outbox; will be retried on the next drain.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await _db.DisposeAsync();
        }
    }
}
