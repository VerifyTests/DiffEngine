global using System.Runtime.CompilerServices;
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Headless;
global using Avalonia.Themes.Fluent;
global using Avalonia.Threading;
global using DiffEngineTray;

[assembly: ParallelLimiter<SingleThreadedLimit>]

public class SingleThreadedLimit :
    TUnit.Core.Interfaces.IParallelLimit
{
    public int Limit => 1;
}
