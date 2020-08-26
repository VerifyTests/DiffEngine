using System;

class MovePayload
{
    public string Temp { get; set; } = null!;
    public string Target { get; set; } = null!;
    public bool CanKill { get; set; }
    public int? ProcessId { get; set; }
    public DateTime? ProcessStartTime { get; set; }
}