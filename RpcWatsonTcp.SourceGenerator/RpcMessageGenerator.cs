using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RpcWatsonTcp.SourceGenerator
{
    /// <summary>
    /// Roslyn incremental source generator.
    /// Reads *.proto files registered as AdditionalFiles and emits one C# source file per
    /// proto file containing partial classes with [GenerateShape] and IRequest/IReply interfaces.
    /// </summary>
    [Generator]
    public sealed class RpcMessageGenerator : IIncrementalGenerator
    {
        private const string DefaultNamespace = "RpcWatsonTcp.Generated";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Read the RpcProtoNamespace MSBuild property (optional)
            IncrementalValueProvider<string> namespaceProp =
                context.AnalyzerConfigOptionsProvider.Select(static (opts, _) =>
                {
                    opts.GlobalOptions.TryGetValue("build_property.RpcProtoNamespace", out string? ns);
                    return string.IsNullOrWhiteSpace(ns) ? DefaultNamespace : ns!;
                });

            // Filter AdditionalFiles to *.proto
            IncrementalValuesProvider<AdditionalText> protoFiles =
                context.AdditionalTextsProvider.Where(static f =>
                    f.Path.EndsWith(".proto", StringComparison.OrdinalIgnoreCase));

            // Combine each proto file with the namespace property
            IncrementalValuesProvider<(AdditionalText ProtoFile, string Namespace)> combined =
                protoFiles.Combine(namespaceProp);

            // For each proto file, parse and emit C#
            context.RegisterSourceOutput(combined, static (spc, pair) =>
            {
                AdditionalText protoFile = pair.ProtoFile;
                string ns = pair.Namespace;

                SourceText? sourceText = protoFile.GetText(spc.CancellationToken);
                if (sourceText is null) return;

                string protoText = sourceText.ToString();
                ProtoFile model;
                try
                {
                    model = ProtoParser.Parse(protoText);
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "RPCGEN001",
                            title: "Proto parse error",
                            messageFormat: "Failed to parse '{0}': {1}",
                            category: "RpcWatsonTcp.SourceGenerator",
                            defaultSeverity: DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None,
                        Path.GetFileName(protoFile.Path),
                        ex.Message));
                    return;
                }

                if (model.Messages.Count == 0 && model.Enums.Count == 0)
                    return; // Nothing to emit

                string generatedCs = CSharpEmitter.Emit(model, ns);
                string hintName = Path.GetFileNameWithoutExtension(protoFile.Path) + ".g.cs";

                spc.AddSource(hintName, SourceText.From(generatedCs, Encoding.UTF8));
            });
        }
    }
}
