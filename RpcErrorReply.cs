using Nerdbank.MessagePack;
using PolyType;

namespace RpcWatsonTcp
{
    [MessagePackObject]
    [GenerateShape]
    public partial class RpcErrorReply : IReply
    {
        [Key(0)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Full name of the exception type (e.g. "System.InvalidOperationException"). Null for unknown types.</summary>
        [Key(1)]
        public string? ExceptionType { get; set; }
    }
}
