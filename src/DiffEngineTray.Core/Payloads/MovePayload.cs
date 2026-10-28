class MovePayload
{
    public string Temp { get; set; } = null!;
    public string Target { get; set; } = null!;
    public string? Exe { get; set; } = null!;
    public string? Arguments { get; set; } = null!;
    public bool CanKill { get; set; }
    public int? ProcessId { get; set; }

    /// <summary>
    /// The received file of the pending move this one was derived from, from a library that says
    /// so: a page of a document whose document is pending too. Null from one that does not, and
    /// for a file that stands alone.
    /// </summary>
    public string? Source { get; set; }
}