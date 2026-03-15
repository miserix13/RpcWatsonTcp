using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace RpcWatsonTcp.Benchmarks;

public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithInvocationCount(16)
            .WithUnrollFactor(16));

        AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
        AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
        AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub);
    }
}
