using System.Net.NetworkInformation;
using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// What a passing inline verification costs with somebody owning the queue, which is a machine
// with a tray running: one settle each, told to the owner whether or not it holds anything.
//
// The time is the small part. A settle that is a connection of its own leaves that connection's
// port in TIME_WAIT once it closes, since the client is the side that closes first, and Windows
// has about 16,000 such ports and keeps each for two minutes. PortsLeftWaiting is how many one
// settle leaves behind, counted from the operating system's table once the run is over.
//
// So where every settle connects, run this with a count that stays well inside that range, or it
// uses up the ports of the machine it runs on:
//
//   dotnet run -c Release --project src/DiffEngine.Benchmarks -- --filter "*SettleBenchmarks*" --invocationCount 512 --unrollFactor 16 --warmupCount 1 --iterationCount 3
//
// Never the real port, which a tray on the machine running this owns: the owner is one this
// process binds on a port the operating system picks, and the variable is pointed at it.
[MemoryDiagnoser]
public class SettleBenchmarks
{
    static readonly ViewerMessage settle = new(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));

    ViewerServer server = null!;
    CancellationTokenSource cancel = null!;
    // Null when the variable was not set, which is what putting it back has to restore
    string previousPort = null!;
    int settles;

    [GlobalSetup]
    public void Setup()
    {
        if (!ViewerServer.TryBind(0, out var bound))
        {
            throw new("Could not bind an ephemeral port.");
        }

        server = bound;
        cancel = new();
        _ = server.Listen(_ => ViewerResponse.Success(), cancel.Token);
        previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());
        ViewerClient.ForgetUnowned();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        var waiting = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Count(_ => _.RemoteEndPoint.Port == server.Port &&
                        _.State == TcpState.TimeWait);
        Console.WriteLine($"// PortsLeftWaiting: {waiting} after {settles} settles");
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
        cancel.Cancel();
        server.Dispose();
        cancel.Dispose();
        ViewerClient.ForgetUnowned();
    }

    // DiffRunner.SettleInline's send, without its check for a build server
    [Benchmark]
    public bool Settle()
    {
        settles++;
        return ViewerClient.TrySend(settle);
    }
}
