using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// Run with:  dotnet run -c Release --project src/DiffEngine.Benchmarks -- --filter *
// Filter e.g.:  dotnet run -c Release --project src/DiffEngine.Benchmarks -- --filter *ProcessScan*
//
// The library's benchmarks. The viewer's model has its own project, DiffEngineViewer.Benchmarks,
// since the viewer links DiffEngine's sources and so declares the same type names; the WinForms
// head has a third, DiffEngineViewer.Windows.Benchmarks, because it only builds for Windows.
//
// In process, which is not BenchmarkDotNet's default. Its default generates a project under bin/
// and builds it with one OutDir for everything that project references, and DiffEngine references
// the three viewer heads for build ordering. All three are named DiffEngineViewer, so they
// overwrite one another's files there and the build fails. Nothing is generated or built this way,
// so a benchmark measures the assemblies this project was built against.
//
// A short run: three warmups and three iterations. These guard against costs that are out by
// multiples, which that settles; pass --iterationCount and --warmupCount for a closer comparison.
var config = DefaultConfig.Instance
    .AddJob(
        Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance));
BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);
