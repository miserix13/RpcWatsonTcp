using Nerdbank.MessagePack;
using PolyType;

namespace RpcWatsonTcp
{
    /// <summary>
    /// Thin wrapper around <see cref="MessagePackSerializer"/> for use within the library.
    /// </summary>
    internal static class RpcSerializer
    {
        private static readonly MessagePackSerializer _serializer = new();

        // ── Envelope (library-owned type; implements IShapeable<RpcEnvelope> via [GenerateShape]) ──

        public static byte[] SerializeEnvelope(RpcEnvelope envelope) =>
            _serializer.Serialize(in envelope)
            ?? throw new InvalidOperationException("Serialized RpcEnvelope produced null bytes.");

        public static RpcEnvelope DeserializeEnvelope(byte[] data) =>
            _serializer.Deserialize<RpcEnvelope>(data)
            ?? throw new RpcException(new RpcErrorReply { Message = "Received null envelope." });

        // ── Error reply (library-owned type) ──

        public static byte[] SerializeErrorReply(RpcErrorReply error) =>
            _serializer.Serialize(in error)
            ?? throw new InvalidOperationException("Serialized RpcErrorReply produced null bytes.");

        public static RpcErrorReply DeserializeErrorReply(byte[] data) =>
            _serializer.Deserialize<RpcErrorReply>(data)
            ?? throw new InvalidOperationException("Received null error reply.");

        // ── User-defined request / reply types (T must implement IShapeable<T> via [GenerateShape]) ──

        public static byte[] Serialize<T>(T value) where T : IShapeable<T> =>
            _serializer.Serialize(in value)
            ?? throw new InvalidOperationException($"Serialized {typeof(T).Name} produced null bytes.");

        public static T Deserialize<T>(byte[] data) where T : IShapeable<T> =>
            _serializer.Deserialize<T>(data)
            ?? throw new InvalidOperationException($"Deserialized null value for type {typeof(T).Name}.");
    }
}
