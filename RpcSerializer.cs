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

        // ── Envelope (library-owned type; uses generated IShapeable<RpcEnvelope>) ──

        public static byte[] SerializeEnvelope(RpcEnvelope envelope) =>
            _serializer.Serialize(envelope);

        public static RpcEnvelope DeserializeEnvelope(byte[] data) =>
            _serializer.Deserialize<RpcEnvelope>(data)
            ?? throw new RpcException(new RpcErrorReply { Message = "Received null envelope." });

        // ── Error reply (library-owned type) ──

        public static byte[] SerializeErrorReply(RpcErrorReply error) =>
            _serializer.Serialize(error);

        public static RpcErrorReply DeserializeErrorReply(byte[] data) =>
            _serializer.Deserialize<RpcErrorReply>(data)
            ?? throw new InvalidOperationException("Received null error reply.");

        // ── User-defined request / reply types (shapes provided at runtime) ──

        public static byte[] Serialize<T>(T value, ITypeShapeProvider provider) =>
            _serializer.Serialize(value, provider);

        public static T Deserialize<T>(byte[] data, ITypeShapeProvider provider) =>
            _serializer.Deserialize<T>(data, provider)
            ?? throw new InvalidOperationException($"Deserialized null value for type {typeof(T).Name}.");
    }
}
