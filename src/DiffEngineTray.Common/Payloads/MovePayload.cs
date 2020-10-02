class MovePayload
{
    public string Temp { get; set; } = null!;
    public string Target { get; set; } = null!;
    public string Exe { get; set; } = null!;
    public string Arguments { get; set; } = null!;
    public bool CanKill { get; set; }
    public int? ProcessId { get; set; }
}