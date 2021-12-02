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
#if (!NET5_0_OR_GREATER)
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
#endif