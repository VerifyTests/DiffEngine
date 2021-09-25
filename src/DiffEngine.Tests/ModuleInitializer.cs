using DiffEngine;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Logging.Enable();
        DiffRunner.Disabled = false;
    }
}