using System;
using System.Xml;
using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

public class WsdlEmitterTests
{
    private static ProtoFile BuildFile(params (string name, (string type, string fieldName, bool repeated)[] fields)[] messages)
    {
        var file = new ProtoFile();
        foreach (var (name, fields) in messages)
        {
            var msg = new ProtoMessage(name);
            int n = 1;
            foreach (var (type, fieldName, repeated) in fields)
                msg.Fields.Add(new ProtoField(fieldName, type, n++, repeated));
            file.Messages.Add(msg);
        }
        return file;
    }

    // ── valid XML ─────────────────────────────────────────────────────────────

    [Fact]
    public void Output_Is_Valid_Xml()
    {
        var file = BuildFile(("PingRequest", new[] { ("string", "message", false) }));
        string wsdl = WsdlEmitter.Emit(file, "Test");
        var doc = new XmlDocument();
        doc.LoadXml(wsdl); // throws if not valid XML
        Assert.NotNull(doc.DocumentElement);
        Assert.Equal("definitions", doc.DocumentElement!.LocalName);
    }

    // ── types section ─────────────────────────────────────────────────────────

    [Fact]
    public void Each_Message_Has_ComplexType()
    {
        var file = BuildFile(
            ("GetUserRequest", new[] { ("string", "id",   false) }),
            ("GetUserReply",   new[] { ("string", "name", false) }));
        string wsdl = WsdlEmitter.Emit(file, "Test");
        Assert.Contains("complexType name=\"GetUserRequest\"", wsdl);
        Assert.Contains("complexType name=\"GetUserReply\"", wsdl);
    }

    [Fact]
    public void Enum_Has_SimpleType()
    {
        var file = new ProtoFile();
        var e = new ProtoEnum("Status");
        e.Values.Add(new ProtoEnumValue("UNKNOWN", 0));
        e.Values.Add(new ProtoEnumValue("ACTIVE",  1));
        file.Enums.Add(e);
        string wsdl = WsdlEmitter.Emit(file, "Test");
        Assert.Contains("simpleType name=\"Status\"", wsdl);
        Assert.Contains("enumeration value=\"Unknown\"", wsdl);
        Assert.Contains("enumeration value=\"Active\"", wsdl);
    }

    // ── portType section ──────────────────────────────────────────────────────

    [Fact]
    public void PortType_Has_Operation_Per_Request()
    {
        var file = BuildFile(
            ("GetUserRequest", new[] { ("string", "id", false) }),
            ("GetUserReply",   new[] { ("string", "name", false) }));
        string wsdl = WsdlEmitter.Emit(file, "Users");
        Assert.Contains("portType name=\"UsersPortType\"", wsdl);
        Assert.Contains("operation name=\"GetUser\"", wsdl);
    }

    // ── service section ───────────────────────────────────────────────────────

    [Fact]
    public void Service_Element_With_Soap_Address()
    {
        var file = BuildFile(("PingRequest", new[] { ("string", "msg", false) }));
        string wsdl = WsdlEmitter.Emit(file, "Test");
        Assert.Contains("service name=", wsdl);
        Assert.Contains("soap:address", wsdl);
        Assert.Contains("localhost", wsdl);
    }

    // ── XSD type mapping ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("string",  "xs:string")]
    [InlineData("int32",   "xs:int")]
    [InlineData("int64",   "xs:long")]
    [InlineData("uint32",  "xs:unsignedInt")]
    [InlineData("uint64",  "xs:unsignedLong")]
    [InlineData("float",   "xs:float")]
    [InlineData("double",  "xs:double")]
    [InlineData("bool",    "xs:boolean")]
    [InlineData("bytes",   "xs:base64Binary")]
    public void Scalar_Types_Map_To_Correct_XSD(string protoType, string expectedXsd)
    {
        var file = BuildFile(("FooRequest", new[] { (protoType, "value", false) }));
        string wsdl = WsdlEmitter.Emit(file, "Test");
        Assert.Contains("type=\"" + expectedXsd + "\"", wsdl);
    }

    // ── repeated fields ───────────────────────────────────────────────────────

    [Fact]
    public void Repeated_Field_Has_MaxOccurs_Unbounded()
    {
        var file = BuildFile(("ListRequest", new[] { ("string", "ids", true) }));
        string wsdl = WsdlEmitter.Emit(file, "Test");
        Assert.Contains("maxOccurs=\"unbounded\"", wsdl);
    }
}
