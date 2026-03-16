using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RpcWatsonTcp.SourceGenerator;
using Xunit;

namespace RpcWatsonTcp.SourceGenerator.Tests;

/// <summary>
/// Verifies that <see cref="RpcMessageGenerator"/> emits a *.specs.g.cs file alongside
/// the regular *.g.cs file, containing OpenApiJson, OpenApiYaml, and Wsdl string properties.
/// </summary>
public class SpecsGeneratorTests
{
    private static (System.Collections.Generic.IReadOnlyList<string> sources, System.Collections.Generic.IReadOnlyList<Diagnostic> diagnostics) Run(
        string protoContent,
        string protoFileName = "messages.proto",
        string? rpcProtoNamespace = null)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText(protoFileName, protoContent);
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
        var sources = result.GeneratedTrees.Select(t => t.GetText().ToString()).ToList();
        return (sources, result.Diagnostics);
    }

    // ── specs file emitted ────────────────────────────────────────────────────

    [Fact]
    public void Generator_Emits_Specs_File_Alongside_Messages_File()
    {
        const string proto = "message PingRequest { string msg = 1; }";
        var (sources, _) = Run(proto);
        // Expect 2 files: messages.g.cs and messages.specs.g.cs
        Assert.Equal(2, sources.Count);
    }

    [Fact]
    public void Specs_File_Contains_OpenApiJson_Property()
    {
        const string proto = "message PingRequest { string msg = 1; }";
        var (sources, _) = Run(proto);
        string specsFile = sources.First(s => s.Contains("Specs"));
        Assert.Contains("OpenApiJson", specsFile);
    }

    [Fact]
    public void Specs_File_Contains_OpenApiYaml_Property()
    {
        const string proto = "message PingRequest { string msg = 1; }";
        var (sources, _) = Run(proto);
        string specsFile = sources.First(s => s.Contains("Specs"));
        Assert.Contains("OpenApiYaml", specsFile);
    }

    [Fact]
    public void Specs_File_Contains_Wsdl_Property()
    {
        const string proto = "message PingRequest { string msg = 1; }";
        var (sources, _) = Run(proto);
        string specsFile = sources.First(s => s.Contains("Specs"));
        Assert.Contains("Wsdl", specsFile);
    }

    // ── class naming ──────────────────────────────────────────────────────────

    [Fact]
    public void Specs_Class_Name_Derived_From_Proto_Filename()
    {
        const string proto = "message PingRequest { string msg = 1; }";
        var (sources, _) = Run(proto, "my_service.proto");
        string specsFile = sources.First(s => s.Contains("Specs"));
        Assert.Contains("MyServiceSpecs", specsFile);
    }

    // ── namespace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Specs_Class_Uses_Custom_Namespace()
    {
        const string proto = "message PingRequest { }";
        var (sources, _) = Run(proto, rpcProtoNamespace: "My.Namespace");
        string specsFile = sources.First(s => s.Contains("Specs"));
        Assert.Contains("namespace My.Namespace", specsFile);
    }

    // ── empty proto ───────────────────────────────────────────────────────────

    [Fact]
    public void Empty_Proto_Produces_No_Specs_File()
    {
        const string proto = "syntax = \"proto3\";";
        var (sources, _) = Run(proto);
        Assert.Empty(sources);
    }
}
