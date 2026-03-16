using Nerdbank.MessagePack;
using PolyType;

namespace RpcWatsonTcp
{
    [GenerateShape]
    public partial class RpcEnvelope
    {
        [Key(0)]
        public Guid MessageId { get; set; }

        /// <summary>Assembly-qualified name of the request or reply type inside <see cref="Payload"/>.</summary>
        [Key(1)]
        public string TypeName { get; set; } = string.Empty;

        /// <summary>MessagePack-serialized <see cref="IRequest"/> or <see cref="IReply"/>.</summary>
        [Key(2)]
        public byte[] Payload { get; set; } = [];

        /// <summary>True when <see cref="Payload"/> contains an <see cref="RpcErrorReply"/>.</summary>
        [Key(3)]
        public bool IsError { get; set; }

        /// <summary>True when this envelope carries an application-layer authentication request or reply.</summary>
        [Key(4)]
        public bool IsAuth { get; set; }
    }
}
