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
        private readonly ITypeShapeProvider _shapes;

        public RpcServer(
            RpcServerOptions options,
            IHandlerRegistry registry,
            IServiceProvider serviceProvider,
            ITypeShapeProvider shapes)
        {
            _registry = registry;
            _serviceProvider = serviceProvider;
            _shapes = shapes;
            _tcpServer = new WatsonTcpServer(options.IpAddress, options.Port);
            _tcpServer.Events.MessageReceived += OnMessageReceived;
        }

        public void Start() => _tcpServer.Start();

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
                    requestEnvelope.Payload, _shapes, CancellationToken.None);
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
            await Task.Run(() => _tcpServer.Dispose());
        }
    }
}
