using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

public class RpcSerializerTests
{
    // ── RpcEnvelope ──────────────────────────────────────────────────────────

    [Fact]
    public void Envelope_RoundTrip_PreservesAllFields()
    {
        var original = new RpcEnvelope
        {
            MessageId = Guid.NewGuid(),
            TypeName = "Some.Type, Some.Assembly",
            Payload = [1, 2, 3, 4],
            IsError = true
        };

        byte[] bytes = RpcSerializer.SerializeEnvelope(original);
        RpcEnvelope restored = RpcSerializer.DeserializeEnvelope(bytes);

        Assert.Equal(original.MessageId, restored.MessageId);
        Assert.Equal(original.TypeName, restored.TypeName);
        Assert.Equal(original.Payload, restored.Payload);
        Assert.Equal(original.IsError, restored.IsError);
    }

    [Fact]
    public void Envelope_EmptyPayload_RoundTrips()
    {
        var original = new RpcEnvelope { MessageId = Guid.NewGuid(), Payload = [] };

        byte[] bytes = RpcSerializer.SerializeEnvelope(original);
        RpcEnvelope restored = RpcSerializer.DeserializeEnvelope(bytes);

        Assert.Empty(restored.Payload);
        Assert.False(restored.IsError);
    }

    // ── RpcErrorReply ────────────────────────────────────────────────────────

    [Fact]
    public void ErrorReply_RoundTrip_PreservesFields()
    {
        var original = new RpcErrorReply
        {
            Message = "Handler failed",
            ExceptionType = "System.ArgumentNullException"
        };

        byte[] bytes = RpcSerializer.SerializeErrorReply(original);
        RpcErrorReply restored = RpcSerializer.DeserializeErrorReply(bytes);

        Assert.Equal(original.Message, restored.Message);
        Assert.Equal(original.ExceptionType, restored.ExceptionType);
    }

    [Fact]
    public void ErrorReply_NullExceptionType_RoundTrips()
    {
        var original = new RpcErrorReply { Message = "Unknown error", ExceptionType = null };

        byte[] bytes = RpcSerializer.SerializeErrorReply(original);
        RpcErrorReply restored = RpcSerializer.DeserializeErrorReply(bytes);

        Assert.Equal("Unknown error", restored.Message);
        Assert.Null(restored.ExceptionType);
    }

    // ── User-defined types ───────────────────────────────────────────────────

    [Fact]
    public void Serialize_UserType_RoundTrips()
    {
        var request = new PingRequest { Message = "hello" };

        byte[] bytes = RpcSerializer.Serialize(request);
        PingRequest restored = RpcSerializer.Deserialize<PingRequest>(bytes);

        Assert.Equal("hello", restored.Message);
    }

    [Fact]
    public void Serialize_UserReply_RoundTrips()
    {
        var reply = new PingReply { Echo = "world" };

        byte[] bytes = RpcSerializer.Serialize(reply);
        PingReply restored = RpcSerializer.Deserialize<PingReply>(bytes);

        Assert.Equal("world", restored.Echo);
    }

    [Fact]
    public void SerializeEnvelope_ProducesNonEmptyBytes()
    {
        var envelope = new RpcEnvelope { MessageId = Guid.NewGuid() };
        byte[] bytes = RpcSerializer.SerializeEnvelope(envelope);
        Assert.NotEmpty(bytes);
    }
}
