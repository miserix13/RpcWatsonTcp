using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

/// <summary>
/// Drives the RpcMessageGenerator through a lightweight in-process CSharpGeneratorDriver.
/// These tests verify the emitted C# content without requiring a full build of the consuming project.
/// </summary>
public class GeneratorSnapshotTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (IReadOnlyList<string> sources, IReadOnlyList<Diagnostic> diagnostics) Run(
        string protoContent,
        string? rpcProtoNamespace = null)
    {
        // Minimal compilation — we only need the syntax tree to satisfy Roslyn; no real references needed
        // because the generator only reads AdditionalText, not semantic symbols.
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText("") },
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText("messages.proto", protoContent);

        var optionsProvider = rpcProtoNamespace is not null
            ? TestAnalyzerConfigOptionsProvider.WithBuildProperty("RpcProtoNamespace", rpcProtoNamespace)
            : TestAnalyzerConfigOptionsProvider.Empty;

        var generator = new RpcMessageGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: new[] { (AdditionalText)additionalText },
            optionsProvider: optionsProvider);

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        var sources = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToList();
        var diagnostics = result.Diagnostics;
        return (sources, diagnostics);
    }

    // ── basic message emission ────────────────────────────────────────────────

    [Fact]
    public void Generate_SimpleRequest_EmitsIRequest()
    {
        const string proto = """
            syntax = "proto3";
            message GetUserRequest {
              string user_id = 1;
            }
            """;

        var (sources, _) = Run(proto);

        Assert.Single(sources);
        string src = sources[0];
        Assert.Contains("[GenerateShape]", src);
        Assert.Contains("public partial class GetUserRequest : IRequest", src);
        Assert.Contains("public string UserId { get; set; } = string.Empty;", src);
    }

    [Fact]
    public void Generate_SimpleReply_EmitsIReply()
    {
        const string proto = """
            syntax = "proto3";
            message GetUserReply {
              string name = 1;
            }
            """;

        var (sources, _) = Run(proto);
        Assert.Single(sources);
        Assert.Contains("public partial class GetUserReply : IReply", sources[0]);
    }

    [Fact]
    public void Generate_NamingConvention_Response_EmitsIReply()
    {
        const string proto = "message CreateUserResponse { bool ok = 1; }";
        var (sources, _) = Run(proto);
        Assert.Contains("public partial class CreateUserResponse : IReply", sources[0]);
    }

    [Fact]
    public void Generate_PlainMessage_NoRpcInterface()
    {
        const string proto = "message Address { string street = 1; }";
        var (sources, _) = Run(proto);
        // should NOT have ": IRequest" or ": IReply"
        Assert.DoesNotContain(": IRequest", sources[0]);
        Assert.DoesNotContain(": IReply", sources[0]);
        Assert.Contains("[GenerateShape]", sources[0]);
    }

    // ── repeated fields ───────────────────────────────────────────────────────

    [Fact]
    public void Generate_RepeatedStringField_EmitsList()
    {
        const string proto = """
            message ListReply {
              repeated string items = 1;
            }
            """;
        var (sources, _) = Run(proto);
        Assert.Contains("List<string>", sources[0]);
        Assert.Contains("new List<string>()", sources[0]);
    }

    // ── enums ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_TopLevelEnum_EmittedInNamespace()
    {
        const string proto = """
            enum Status {
              UNKNOWN  = 0;
              ACTIVE   = 1;
            }
            """;
        var (sources, _) = Run(proto);
        // Enum only — still produces a file
        Assert.Single(sources);
        Assert.Contains("public enum Status", sources[0]);
        Assert.Contains("Unknown = 0,", sources[0]);
        Assert.Contains("Active = 1,", sources[0]);
    }

    [Fact]
    public void Generate_EnumFieldInMessage_UsesEnumType()
    {
        const string proto = """
            enum Status { UNKNOWN = 0; ACTIVE = 1; }
            message UserRequest { Status status = 1; }
            """;
        var (sources, _) = Run(proto);
        // The field type should be Status (or Status?)
        Assert.Contains("Status", sources[0]);
    }

    // ── namespace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_CustomNamespace_IsUsed()
    {
        const string proto = "message PingRequest { }";
        var (sources, _) = Run(proto, rpcProtoNamespace: "MyApp.Messages");
        Assert.Contains("namespace MyApp.Messages", sources[0]);
    }

    [Fact]
    public void Generate_NoNamespaceProperty_UsesDefault()
    {
        const string proto = "message PingRequest { }";
        var (sources, _) = Run(proto);
        Assert.Contains("namespace RpcWatsonTcp.Generated", sources[0]);
    }

    // ── header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_OutputContainsAutoGeneratedHeader()
    {
        const string proto = "message PingRequest { }";
        var (sources, _) = Run(proto);
        Assert.Contains("// <auto-generated/>", sources[0]);
        Assert.Contains("#nullable enable", sources[0]);
    }

    // ── empty proto ───────────────────────────────────────────────────────────

    [Fact]
    public void Generate_EmptyProto_ProducesNoOutput()
    {
        const string proto = "syntax = \"proto3\";";
        var (sources, diags) = Run(proto);
        Assert.Empty(sources);
        Assert.Empty(diags);
    }

    // ── diagnostics ───────────────────────────────────────────────────────────

    [Fact]
    public void Generate_MalformedField_ProducesNoDiagnosticAndSkips()
    {
        // The parser is lenient — unknown syntax lines are silently skipped
        const string proto = """
            message PingRequest {
              this is not valid proto;
            }
            """;
        // Should not throw and should not crash the generator
        var (_, diags) = Run(proto);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ── type mapping ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("int32", "int", "0")]
    [InlineData("int64", "long", "0")]
    [InlineData("uint32", "uint", "0")]
    [InlineData("uint64", "ulong", "0")]
    [InlineData("float", "float", "0f")]
    [InlineData("double", "double", "0.0")]
    [InlineData("bool", "bool", "false")]
    [InlineData("string", "string", "string.Empty")]
    public void Generate_ScalarType_MappedCorrectly(string protoType, string csType, string defaultVal)
    {
        string proto = $"message FooRequest {{ {protoType} value = 1; }}";
        var (sources, _) = Run(proto);
        Assert.Contains(csType + " Value { get; set; } = " + defaultVal, sources[0]);
    }

    [Fact]
    public void Generate_BytesField_UsesByteArray()
    {
        const string proto = "message FooRequest { bytes data = 1; }";
        var (sources, _) = Run(proto);
        Assert.Contains("byte[]", sources[0]);
        Assert.Contains("System.Array.Empty<byte>()", sources[0]);
    }
}

// ── test helpers ─────────────────────────────────────────────────────────────

internal sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
{
    public override string Path => path;
    public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
        => SourceText.From(content);
}

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    public static readonly TestAnalyzerConfigOptionsProvider Empty = new(new Dictionary<string, string>());

    private readonly Dictionary<string, string> _globalProperties;

    private TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalProperties)
    {
        _globalProperties = globalProperties;
    }

    public static TestAnalyzerConfigOptionsProvider WithBuildProperty(string key, string value)
        => new(new Dictionary<string, string> { [$"build_property.{key}"] = value });

    public override AnalyzerConfigOptions GlobalOptions => new DictionaryOptions(_globalProperties);
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => DictionaryOptions.Empty;

    private sealed class DictionaryOptions(Dictionary<string, string> dict) : AnalyzerConfigOptions
    {
        public static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());
        public override bool TryGetValue(string key, out string value)
            => dict.TryGetValue(key, out value!);
    }
}
