[assembly: NonParallelizable]

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Source.Init();
        Logging.Enable();
        DiffRunner.Disabled = false;
    }
}