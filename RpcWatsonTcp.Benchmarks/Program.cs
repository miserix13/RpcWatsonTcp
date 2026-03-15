using BenchmarkDotNet.Running;
using RpcWatsonTcp.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RpcRoundTripBenchmark).Assembly).Run(args);
