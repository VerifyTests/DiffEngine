using DiffEngine;
using Xunit;

[GlobalSetUp]
public static class GlobalSetup
{
    public static void Setup()
    {
        Logging.Enable();
    }
}