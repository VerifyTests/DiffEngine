[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        FileExtensions.AddTextFileConvention(_ => _.EndsWith(".txtConvention".AsSpan()));
        Logging.Enable();
        DiffRunner.Disabled = false;
    }
}