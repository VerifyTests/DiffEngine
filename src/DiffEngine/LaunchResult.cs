namespace DiffEngine
{
    public enum LaunchResult
    {
        NoEmptyFileForExtension,
        AlreadyRunningAndSupportsRefresh,
        StartedNewInstance,
        TooManyRunningDiffTools,
        NoDiffToolForExtension
    }
}