global using System.Diagnostics;
global using System.Net;
global using System.Net.Sockets;
global using EmptyFiles;

[assembly: ParallelLimiter<SingleThreadedLimit>]

public class SingleThreadedLimit : TUnit.Core.Interfaces.IParallelLimit
{
    public int Limit => 1;
}
