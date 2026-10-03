using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// Run with:  dotnet run -c Release --project src/DiffEngineViewer.Benchmarks -- --filter *
// Filter e.g.:  dotnet run -c Release --project src/DiffEngineViewer.Benchmarks -- --filter *Frame*
//
// The viewer's model: everything a frame costs before a head draws it. Nothing here opens a
// window or binds a port.
//
// In process and a short run, for the reasons DiffEngine.Benchmarks' Program.cs gives.
var config = DefaultConfig.Instance
    .AddJob(
        Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance));
BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);
