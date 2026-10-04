class DeletePayload
{
    public string File { get; set; } = null!;

    /// <inheritdoc cref="MovePayload.Source"/>
    public string? Source { get; set; }
}