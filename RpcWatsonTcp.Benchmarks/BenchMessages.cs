using PolyType;
using RpcWatsonTcp;

namespace RpcWatsonTcp.Benchmarks;

[GenerateShape]
public partial class BenchRequest : IRequest
{
    public string Payload { get; set; } = string.Empty;
}

[GenerateShape]
public partial class BenchReply : IReply
{
    public string Echo { get; set; } = string.Empty;
}

[GenerateShape]
public partial class BenchCredential : ICredential
{
    public string Token { get; set; } = string.Empty;
}
