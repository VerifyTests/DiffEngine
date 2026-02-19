global using System.Diagnostics;

[assembly: ParallelLimiter<SingleThreadedLimit>]

public class SingleThreadedLimit : TUnit.Core.Interfaces.IParallelLimit
{
    public int Limit => 1;
}
