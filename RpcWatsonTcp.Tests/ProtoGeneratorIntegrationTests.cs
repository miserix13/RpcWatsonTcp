using RpcWatsonTcp;
using RpcWatsonTcp.Tests.Generated;

namespace RpcWatsonTcp.Tests;

/// <summary>
/// Verifies that types emitted by the Protobuf IDL source generator have the correct
/// shape at compile time: right namespace, right interface implementations, right
/// property names and default values.
///
/// Note: a full RPC round-trip test is not possible here because Roslyn source generators
/// cannot see output from other source generators in the same compilation pass, so
/// PolyType's [GenerateShape] processor does not produce IShapeable&lt;T&gt; for
/// generator-emitted types. To use generated types in RPC calls, declare a thin
/// hand-written partial class (with [GenerateShape]) in your own project files.
/// </summary>
public class ProtoGeneratorIntegrationTests
{
    [Fact]
    public void Generated_EchoProtoRequest_ImplementsIRequest()
    {
        var req = new EchoProtoRequest();
        Assert.IsAssignableFrom<IRequest>(req);
    }

    [Fact]
    public void Generated_EchoProtoReply_ImplementsIReply()
    {
        var reply = new EchoProtoReply();
        Assert.IsAssignableFrom<IReply>(reply);
    }

    [Fact]
    public void Generated_EchoProtoRequest_DefaultValues()
    {
        var req = new EchoProtoRequest();
        Assert.Equal(string.Empty, req.Message);
        Assert.Equal(0, req.Count);
    }

    [Fact]
    public void Generated_EchoProtoReply_DefaultValues()
    {
        var reply = new EchoProtoReply();
        Assert.Equal(string.Empty, reply.Echoed);
        Assert.Equal(0, reply.Length);
    }

    [Fact]
    public void Generated_EchoProtoRequest_PropertyAssignment()
    {
        var req = new EchoProtoRequest { Message = "hello", Count = 3 };
        Assert.Equal("hello", req.Message);
        Assert.Equal(3, req.Count);
    }

    [Fact]
    public void Generated_EchoProtoReply_PropertyAssignment()
    {
        var reply = new EchoProtoReply { Echoed = "world", Length = 5 };
        Assert.Equal("world", reply.Echoed);
        Assert.Equal(5, reply.Length);
    }
}
