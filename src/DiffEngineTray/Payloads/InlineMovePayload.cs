class InlineMovePayload
{
    public string Temp { get; set; } = null!;
    public string Target { get; set; } = null!;
    public string PatchFile { get; set; } = null!;
    public string? StagedVerified { get; set; }
}
