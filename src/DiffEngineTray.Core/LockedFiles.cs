record LockedFiles(List<string> Files, List<LockingProcess> Processes)
{
    public string ProcessNames =>
        string.Join(
            ", ",
            Processes
                .Select(_ => _.Name)
                .Distinct());
}