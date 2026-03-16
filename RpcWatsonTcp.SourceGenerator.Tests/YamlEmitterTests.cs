using System;
using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

public class YamlEmitterTests
{
    private static ProtoFile SimpleRequest(string msgName = "PingRequest") =>
        BuildFile((msgName, new[] { ("string", "message", false) }));

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

    // ── header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Yaml_Starts_With_OpenApi_Header()
    {
        string yaml = YamlEmitter.Emit(SimpleRequest(), "Test");
        Assert.StartsWith("openapi:", yaml.TrimStart());
    }

    [Fact]
    public void Yaml_Contains_Info_Section()
    {
        string yaml = YamlEmitter.Emit(SimpleRequest(), "MyService");
        Assert.Contains("info:", yaml);
        Assert.Contains("title:", yaml);
        Assert.Contains("MyService", yaml);
        Assert.Contains("version:", yaml);
    }

    // ── paths ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Yaml_Contains_Paths_Section()
    {
        string yaml = YamlEmitter.Emit(SimpleRequest(), "Test");
        Assert.Contains("paths:", yaml);
        Assert.Contains("/PingRequest:", yaml);
        Assert.Contains("post:", yaml);
    }

    // ── type rendering ────────────────────────────────────────────────────────

    [Fact]
    public void String_Field_Renders_As_Type_String()
    {
        var file = BuildFile(("PingRequest", new[] { ("string", "value", false) }));
        string yaml = YamlEmitter.Emit(file, "Test");
        Assert.Contains("type: string", yaml);
    }

    [Fact]
    public void Integer_Field_Renders_As_Type_Integer()
    {
        var file = BuildFile(("PingRequest", new[] { ("int32", "count", false) }));
        string yaml = YamlEmitter.Emit(file, "Test");
        Assert.Contains("type: integer", yaml);
    }

    [Fact]
    public void Repeated_Field_Renders_As_Array()
    {
        var file = BuildFile(("ListReply", new[] { ("string", "items", true) }));
        string yaml = YamlEmitter.Emit(file, "Test");
        Assert.Contains("type: array", yaml);
        Assert.Contains("items:", yaml);
    }

    // ── no JSON syntax ────────────────────────────────────────────────────────

    [Fact]
    public void Yaml_Does_Not_Contain_Json_Braces_As_Structure()
    {
        // Keys should NOT look like "key": in YAML (they should be key:)
        var file = BuildFile(("GetUserRequest", new[] { ("string", "id", false) }),
                             ("GetUserReply",   new[] { ("string", "name", false) }));
        string yaml = YamlEmitter.Emit(file, "Test");
        // YAML keys should not be double-quoted (except in values)
        // No JSON-style property-colon (": ) except where it's a value string
        Assert.DoesNotContain("\"openapi\":", yaml);
        Assert.DoesNotContain("\"paths\":", yaml);
        Assert.DoesNotContain("\"info\":", yaml);
    }

    // ── enum rendering ────────────────────────────────────────────────────────

    [Fact]
    public void Enum_Values_Render_As_List_Items()
    {
        var file = new ProtoFile();
        var e = new ProtoEnum("Status");
        e.Values.Add(new ProtoEnumValue("UNKNOWN", 0));
        e.Values.Add(new ProtoEnumValue("ACTIVE",  1));
        file.Enums.Add(e);
        string yaml = YamlEmitter.Emit(file, "Test");
        Assert.Contains("type: string", yaml);
        Assert.Contains("enum:", yaml);
        Assert.Contains("- Unknown", yaml);
        Assert.Contains("- Active", yaml);
    }
}
