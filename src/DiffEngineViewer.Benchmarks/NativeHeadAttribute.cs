using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

/// <summary>
/// For a benchmark class that turns the Linux head's window. Leaves its benchmarks out of a run
/// wherever <see cref="NativeHead.Available"/> is false, which is every run on Windows, and adds
/// the columns a frame is judged by to the ones BenchmarkDotNet has: see <see cref="NativeHead"/>
/// for why its own time column is not one of them.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NativeHeadAttribute : Attribute, IConfigSource
{
    public IConfig Config { get; } =
        ManualConfig.CreateEmpty()
            .AddFilter(new SimpleFilter(_ => NativeHead.Available))
            .AddDiagnoser(NativeHeadDiagnoser.Instance);
}

/// <summary>
/// Reports what <see cref="NativeHead"/> counted during the measured part of a run, per run of the
/// benchmark method, beside what the X server spent over the same stretch.
/// </summary>
sealed class NativeHeadDiagnoser : IDiagnoser
{
    public static readonly NativeHeadDiagnoser Instance = new();

    static readonly Column processorTime = new(
        "ProcessorTime",
        "CPU",
        "Processor time this process spent on one operation, on every thread",
        UnitType.Time,
        "ns",
        0);

    static readonly Column serverTime = new(
        "ServerProcessorTime",
        "X server CPU",
        "Processor time the X server spent while one operation ran",
        UnitType.Time,
        "ns",
        1);

    static readonly Column drawn = new(
        "FramesDrawn",
        "Drawn",
        "Frames of one operation that submitted anything to draw",
        UnitType.Dimensionless,
        "Count",
        2);

    static readonly Column triangles = new(
        "Triangles",
        "Triangles",
        "Triangles one operation submitted, as OpenGL counted them",
        UnitType.Dimensionless,
        "Count",
        3);

    readonly Dictionary<BenchmarkCase, (NativeHeadTotals Totals, long Server)> runs = [];
    NativeHeadTotals start;
    long serverStart;

    public IEnumerable<string> Ids =>
        [nameof(NativeHeadDiagnoser)];

    public IEnumerable<IExporter> Exporters =>
        [];

    public IEnumerable<IAnalyser> Analysers =>
        [];

    public RunMode GetRunMode(BenchmarkCase benchmarkCase) =>
        RunMode.NoOverhead;

    public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
    {
        if (signal == HostSignal.BeforeActualRun)
        {
            start = NativeHead.Totals;
            serverStart = NativeHead.ServerProcessorTime();
            return;
        }

        if (signal != HostSignal.AfterActualRun)
        {
            return;
        }

        var server = NativeHead.ServerProcessorTime();
        var end = NativeHead.Totals;
        runs[parameters.BenchmarkCase] = (
            new()
            {
                Operations = end.Operations - start.Operations,
                Turns = end.Turns - start.Turns,
                Drawn = end.Drawn - start.Drawn,
                Triangles = end.Triangles - start.Triangles,
                ProcessorTime = end.ProcessorTime - start.ProcessorTime
            },
            server < 0 || serverStart < 0 ? -1 : server - serverStart);
    }

    public IEnumerable<Metric> ProcessResults(DiagnoserResults results)
    {
        if (!runs.TryGetValue(results.BenchmarkCase, out var run) ||
            run.Totals.Operations == 0)
        {
            yield break;
        }

        // Counted by the benchmark methods themselves rather than taken from the results, so the
        // figures do not rest on which of BenchmarkDotNet's iterations fall between its two signals.
        double operations = run.Totals.Operations;
        yield return new(processorTime, run.Totals.ProcessorTime / operations);
        if (run.Server >= 0)
        {
            yield return new(serverTime, run.Server / operations);
        }

        yield return new(drawn, run.Totals.Drawn / operations);
        yield return new(triangles, run.Totals.Triangles / operations);
    }

    public void DisplayResults(ILogger logger)
    {
    }

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters) =>
        [];

    sealed class Column(string id, string name, string legend, UnitType unitType, string unit, int priority) :
        IMetricDescriptor
    {
        public string Id => id;

        public string DisplayName => name;

        public string Legend => legend;

        public string NumberFormat => "0.##";

        public UnitType UnitType => unitType;

        public string Unit => unit;

        public bool TheGreaterTheBetter => false;

        public int PriorityInCategory => priority;

        public bool GetIsAvailable(Metric metric) =>
            true;
    }
}
