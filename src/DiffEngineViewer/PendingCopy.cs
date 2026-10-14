/// <summary>
/// Text waiting for the clipboard (<see cref="SessionState.Clipboard"/>). Compared by reference:
/// a second copy of the same text is another copy, with attempts of its own.
/// </summary>
/// <param name="What">What was copied, as the status line names it, for saying it was not.</param>
sealed record PendingCopy(string Text, string What)
{
    public bool Equals(PendingCopy? other) =>
        ReferenceEquals(this, other);

    public override int GetHashCode() =>
        RuntimeHelpers.GetHashCode(this);
}
