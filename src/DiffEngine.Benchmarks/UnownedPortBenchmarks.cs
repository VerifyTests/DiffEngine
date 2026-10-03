using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// What it costs to learn that nothing owns the viewer port, which is the ordinary state of a
// machine with no tray: no viewer is open until a snapshot fails. Windows does not refuse a
// loopback connection to a closed port at once, so every one of these was a wait.
//
// Never the real port, which a tray on the machine running this owns. The port is one this
// process binds and does not listen on: nothing answers on it, as nothing answers on a port that
// is free, and nothing else can take it while the benchmark runs, which a port found free and let
// go cannot promise. The variable is pointed at it as well, so that nothing here could reach a
// live owner by leaving a port out.
[MemoryDiagnoser]
public class UnownedPortBenchmarks
{
    static readonly ViewerMessage settle = new(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));
    static readonly ViewerMessage list = new(ViewerVerb.List);

    Socket holder = null!;
    int port;
    // Null when the variable was not set, which is what putting it back has to restore
    string previousPort = null!;

    [GlobalSetup]
    public void Setup()
    {
        holder = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        port = ((IPEndPoint) holder.LocalEndPoint!).Port;
        previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
        holder.Dispose();
    }

    // The launch gate's question: asked before it starts a viewer, and on every poll while the
    // one it started is binding
    [Benchmark]
    public bool Probe() =>
        ViewerClient.IsOwned(port);

    // The first settle, move or delete of a test process, with nothing remembered about the port
    [Benchmark]
    public bool FirstTellingSend()
    {
        ViewerClient.ForgetUnowned();
        return ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
    }

    // The same from AddInlineAsync, whose first send is the async one
    [Benchmark]
    public async Task<bool> FirstTellingSendAsync()
    {
        ViewerClient.ForgetUnowned();
        return await ViewerClient.SendAsync(settle, CancellationToken.None, port, skipIfUnowned: true) == SendOutcome.Accepted;
    }

    // A pending file or a failing snapshot with nothing owning the queue and no viewer that can
    // be started. The gate is held for all of it, so every other caller waits this long too
    [Benchmark]
    public bool GatedCallWithNothingToLaunch() =>
        ViewerLaunchGate.Launch(
            retry: () => false,
            launch: () => null,
            isOwned: () => ViewerClient.IsOwned(port),
            canLaunch: () => true) == ViewerLaunchOutcome.Launched;

    // A host or a review surface asking for the queue, which is never answered from memory
    [Benchmark]
    public bool Ask() =>
        ViewerClient.TrySend(list, out _, port, ViewerClient.ShortTimeout);
}
