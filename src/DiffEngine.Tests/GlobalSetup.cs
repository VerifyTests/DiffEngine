using System.Runtime.CompilerServices;
using DiffEngine;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

public static class GlobalSetup
{
    [ModuleInitializer]
    public static void Setup()
    {
        Logging.Enable();
        DiffRunner.Disabled = false;
    }
}