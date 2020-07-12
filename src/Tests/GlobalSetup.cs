using DiffEngine;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[GlobalSetUp]
public static class GlobalSetup
{
    public static void Setup()
    {
        Logging.Enable();
        DiffRunner.Disabled = false;
    }
}