using PolyType;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Tests;

// Shared test message types — must be partial + [GenerateShape] for Nerdbank.MessagePack.
[GenerateShape]
public partial class PingRequest : IRequest
{
    public string Message { get; set; } = string.Empty;
}

[GenerateShape]
public partial class PingReply : IReply
{
    public string Echo { get; set; } = string.Empty;
}
