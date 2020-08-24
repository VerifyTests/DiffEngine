class MovePayload
{
    public string Temp { get; set; } = null!;
    public string Target { get; set; } = null!;
    public bool IsMdi { get; set; }
    public bool AutoRefresh { get; set; }
    public int ProcessId { get; set; }
}