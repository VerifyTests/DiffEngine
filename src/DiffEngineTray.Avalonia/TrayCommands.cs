namespace DiffEngineTray;

class TrayCommands
{
    public required Action Exit { get; init; }
    public required Action Options { get; init; }
    public required Action OpenLogs { get; init; }
    public required Action Purge { get; init; }
    public required Action RaiseIssue { get; init; }
    public required Action Refresh { get; init; }
}
