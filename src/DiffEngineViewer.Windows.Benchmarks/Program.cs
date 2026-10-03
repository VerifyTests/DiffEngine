using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// Run with:  dotnet run -c Release --project src/DiffEngineViewer.Windows.Benchmarks -- --filter *
// Filter e.g.:  dotnet run -c Release --project src/DiffEngineViewer.Windows.Benchmarks -- --filter *Paint*
//
// The WinForms head: what a paint costs. A project of its own because the head only builds for
// Windows, where DiffEngineViewer.Benchmarks measures the model on every platform. Everything is
// painted into a bitmap, so nothing here shows a window or binds a port.
//
// In process and a short run, for the reasons DiffEngine.Benchmarks' Program.cs gives.
var config = DefaultConfig.Instance
    .AddJob(
        Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance));
BenchmarkSwitcher
    // Not typeof(Program), as the other two have it: the head is an executable with a Program of
    // its own, which this assembly can see, so the name would be ambiguous here.
    .FromAssembly(Assembly.GetExecutingAssembly())
    .Run(args, config);
