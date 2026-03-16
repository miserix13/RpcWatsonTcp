using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

public class ProtoParserTests
{
    // ── package ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_PackageDeclaration_IsCapture()
    {
        const string proto = """
            syntax = "proto3";
            package users;
            message Foo { }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Equal("users", file.Package);
    }

    [Fact]
    public void Parse_NoPackage_PackageIsNull()
    {
        const string proto = """
            syntax = "proto3";
            message Foo { }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Null(file.Package);
    }

    // ── messages ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyMessage_IsAdded()
    {
        const string proto = "message Empty { }";
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Single(file.Messages);
        Assert.Equal("Empty", file.Messages[0].Name);
        Assert.Empty(file.Messages[0].Fields);
    }

    [Fact]
    public void Parse_ScalarFields_AreParsed()
    {
        const string proto = """
            message GetUserRequest {
              string user_id = 1;
              int32  version = 2;
              bool   active  = 3;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        var msg = file.Messages[0];
        Assert.Equal(3, msg.Fields.Count);
        Assert.Equal(("user_id", "string", 1, false),
            (msg.Fields[0].Name, msg.Fields[0].TypeName, msg.Fields[0].Number, msg.Fields[0].Repeated));
        Assert.Equal(("version", "int32", 2, false),
            (msg.Fields[1].Name, msg.Fields[1].TypeName, msg.Fields[1].Number, msg.Fields[1].Repeated));
    }

    [Fact]
    public void Parse_RepeatedField_IsMarkedRepeated()
    {
        const string proto = """
            message GetUserReply {
              repeated string roles = 1;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        var field = file.Messages[0].Fields[0];
        Assert.True(field.Repeated);
        Assert.Equal("string", field.TypeName);
        Assert.Equal("roles", field.Name);
    }

    [Fact]
    public void Parse_MultipleMessages_AllCaptured()
    {
        const string proto = """
            message Foo { string x = 1; }
            message Bar { int32 y = 2; }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Equal(2, file.Messages.Count);
        Assert.Equal("Foo", file.Messages[0].Name);
        Assert.Equal("Bar", file.Messages[1].Name);
    }

    // ── nested ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NestedMessage_IsCaptured()
    {
        const string proto = """
            message Outer {
              message Inner {
                string value = 1;
              }
              Inner inner = 1;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        var outer = file.Messages[0];
        Assert.Single(outer.NestedMessages);
        Assert.Equal("Inner", outer.NestedMessages[0].Name);
        Assert.Single(outer.NestedMessages[0].Fields);
    }

    [Fact]
    public void Parse_NestedEnum_IsCaptured()
    {
        const string proto = """
            message StatusRequest {
              enum Kind {
                UNKNOWN = 0;
                ACTIVE  = 1;
              }
              Kind kind = 1;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        var msg = file.Messages[0];
        Assert.Single(msg.NestedEnums);
        Assert.Equal("Kind", msg.NestedEnums[0].Name);
        Assert.Equal(2, msg.NestedEnums[0].Values.Count);
    }

    // ── enums ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_TopLevelEnum_IsCaptured()
    {
        const string proto = """
            enum Status {
              UNKNOWN  = 0;
              ACTIVE   = 1;
              INACTIVE = 2;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Single(file.Enums);
        var e = file.Enums[0];
        Assert.Equal("Status", e.Name);
        Assert.Equal(3, e.Values.Count);
        Assert.Equal(("UNKNOWN", 0), (e.Values[0].Name, e.Values[0].Number));
    }

    // ── comments ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_LineComments_AreStripped()
    {
        const string proto = """
            // This is a comment
            message Foo {
              string name = 1; // inline comment
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        var msg = file.Messages[0];
        Assert.Single(msg.Fields);
        Assert.Equal("name", msg.Fields[0].Name);
    }

    [Fact]
    public void Parse_BlockComments_AreStripped()
    {
        const string proto = """
            /* header */
            message Foo {
              /* field */ string name = 1;
            }
            """;
        ProtoFile file = ProtoParser.Parse(proto);
        Assert.Single(file.Messages[0].Fields);
    }
}
