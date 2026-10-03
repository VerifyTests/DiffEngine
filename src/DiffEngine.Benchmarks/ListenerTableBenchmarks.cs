using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// What it costs to ask the operating system whether anything is listening on a port, which is
// asked in front of a connect that would otherwise wait two seconds to be refused: by ViewerClient
// for a port it has not just talked to, and by PiperClient on every move and delete.
//
// The answer is no here, which is the answer that has to look at every row there is. What is
// varied is how many connections the machine has open beside the listeners, since that is what
// the cost was found to follow: each loopback connection made here is two rows, one for each end.
//
// Never the real ports, which a tray on the machine running this owns. The port asked about is
// one this process binds and does not listen on, as in UnownedPortBenchmarks.
[MemoryDiagnoser]
public class ListenerTableBenchmarks
{
    [Params(0, 1500)]
    public int Connections;

    Socket holder = null!;
    int port;
    TcpListener listener = null!;
    List<TcpClient> ends = [];

    [GlobalSetup]
    public void Setup()
    {
        holder = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        port = ((IPEndPoint) holder.LocalEndPoint!).Port;

        listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var target = ((IPEndPoint) listener.LocalEndpoint).Port;
        for (var index = 0; index < Connections; index++)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, target);
            ends.Add(client);
            ends.Add(listener.AcceptTcpClient());
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var end in ends)
        {
            end.Dispose();
        }

        ends.Clear();
        listener.Stop();
        holder.Dispose();
    }

    [Benchmark]
    public bool NobodyListening() =>
        ListenerTable.IsHeld(port);
}
