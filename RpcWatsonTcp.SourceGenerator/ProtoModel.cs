using System.Collections.Generic;

namespace RpcWatsonTcp.SourceGenerator
{
    internal sealed class ProtoFile
    {
        public string? Package { get; set; }
        public List<ProtoMessage> Messages { get; } = new List<ProtoMessage>();
        public List<ProtoEnum> Enums { get; } = new List<ProtoEnum>();
    }

    internal sealed class ProtoMessage
    {
        public ProtoMessage(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public List<ProtoField> Fields { get; } = new List<ProtoField>();
        public List<ProtoMessage> NestedMessages { get; } = new List<ProtoMessage>();
        public List<ProtoEnum> NestedEnums { get; } = new List<ProtoEnum>();
    }

    internal sealed class ProtoField
    {
        public ProtoField(string name, string typeName, int number, bool repeated)
        {
            Name = name;
            TypeName = typeName;
            Number = number;
            Repeated = repeated;
        }

        public string Name { get; }
        public string TypeName { get; }
        public int Number { get; }
        public bool Repeated { get; }
    }

    internal sealed class ProtoEnum
    {
        public ProtoEnum(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public List<ProtoEnumValue> Values { get; } = new List<ProtoEnumValue>();
    }

    internal sealed class ProtoEnumValue
    {
        public ProtoEnumValue(string name, int number)
        {
            Name = name;
            Number = number;
        }

        public string Name { get; }
        public int Number { get; }
    }
}
