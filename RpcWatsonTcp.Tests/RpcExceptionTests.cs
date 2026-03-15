using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

public class RpcExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageFromErrorReply()
    {
        var reply = new RpcErrorReply { Message = "Something broke", ExceptionType = "System.InvalidOperationException" };
        var ex = new RpcException(reply);

        Assert.Equal("Something broke", ex.Message);
        Assert.Equal("System.InvalidOperationException", ex.RemoteExceptionType);
    }

    [Fact]
    public void Constructor_HandlesNullExceptionType()
    {
        var reply = new RpcErrorReply { Message = "Oops", ExceptionType = null };
        var ex = new RpcException(reply);

        Assert.Equal("Oops", ex.Message);
        Assert.Null(ex.RemoteExceptionType);
    }
}
