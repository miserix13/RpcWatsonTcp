using System;
using System.Collections.Generic;
using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

public class OpenApiEmitterTests
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

    // ── endpoint paths ────────────────────────────────────────────────────────

    [Fact]
    public void Request_Message_Produces_Post_Path()
    {
        var file = BuildFile(("GetUserRequest", new[] { ("string", "user_id", false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains("\"/GetUserRequest\"", json);
        Assert.Contains("\"post\"", json);
    }

    [Fact]
    public void Reply_Message_Not_Added_As_Path()
    {
        var file = BuildFile(
            ("GetUserRequest", new[] { ("string", "id", false) }),
            ("GetUserReply",   new[] { ("string", "name", false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.DoesNotContain("\"/GetUserReply\"", json);
    }

    [Fact]
    public void Request_Response_Pair_Linked_Correctly()
    {
        var file = BuildFile(
            ("CreateUserRequest",  new[] { ("string", "name", false) }),
            ("CreateUserResponse", new[] { ("bool",   "ok",   false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains("CreateUserResponse", json);
    }

    [Fact]
    public void Plain_Message_Appears_In_Schemas_Not_Paths()
    {
        var file = BuildFile(("Address", new[] { ("string", "street", false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.DoesNotContain("\"/Address\"", json);
        Assert.Contains("\"Address\"", json); // in schemas
    }

    // ── scalar type mapping ───────────────────────────────────────────────────

    [Theory]
    [InlineData("string",  "\"type\":\"string\"")]
    [InlineData("int32",   "\"type\":\"integer\",\"format\":\"int32\"")]
    [InlineData("int64",   "\"type\":\"integer\",\"format\":\"int64\"")]
    [InlineData("uint32",  "\"minimum\":0")]
    [InlineData("float",   "\"type\":\"number\",\"format\":\"float\"")]
    [InlineData("double",  "\"type\":\"number\",\"format\":\"double\"")]
    [InlineData("bool",    "\"type\":\"boolean\"")]
    [InlineData("bytes",   "\"type\":\"string\",\"format\":\"byte\"")]
    public void Scalar_Types_Mapped_Correctly(string protoType, string expectedFragment)
    {
        var file = BuildFile(("FooRequest", new[] { (protoType, "value", false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains(expectedFragment, json);
    }

    // ── repeated fields ───────────────────────────────────────────────────────

    [Fact]
    public void Repeated_Field_Emits_Array_Type()
    {
        var file = BuildFile(("ListReply", new[] { ("string", "items", true) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains("\"type\":\"array\"", json);
        Assert.Contains("\"items\"", json);
    }

    // ── enum schemas ──────────────────────────────────────────────────────────

    [Fact]
    public void Enum_Type_Produces_String_Enum_Schema()
    {
        var file = new ProtoFile();
        var e = new ProtoEnum("Status");
        e.Values.Add(new ProtoEnumValue("UNKNOWN", 0));
        e.Values.Add(new ProtoEnumValue("ACTIVE",  1));
        file.Enums.Add(e);
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains("\"Status\"", json);
        Assert.Contains("\"type\":\"string\"", json);
        Assert.Contains("\"enum\"", json);
        Assert.Contains("\"Unknown\"", json);
        Assert.Contains("\"Active\"", json);
    }

    // ── header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Output_Contains_OpenApi_Version_And_Info()
    {
        var file = BuildFile(("PingRequest", Array.Empty<(string, string, bool)>()));
        string json = OpenApiEmitter.Emit(file, "MyService");
        Assert.Contains("\"openapi\":\"3.0.0\"", json);
        Assert.Contains("\"info\"", json);
        Assert.Contains("\"title\"", json);
        Assert.Contains("MyService", json);
    }

    // ── multiple messages in schemas ──────────────────────────────────────────

    [Fact]
    public void Multiple_Messages_All_In_Schemas()
    {
        var file = BuildFile(
            ("GetUserRequest", new[] { ("string", "id", false) }),
            ("GetUserReply",   new[] { ("string", "name", false) }),
            ("Address",        new[] { ("string", "street", false) }));
        string json = OpenApiEmitter.Emit(file, "Test");
        Assert.Contains("\"GetUserRequest\"", json);
        Assert.Contains("\"GetUserReply\"", json);
        Assert.Contains("\"Address\"", json);
    }
}
