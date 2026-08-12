/// <summary>
/// A single user action. <paramref name="Index"/> is only meaningful for
/// <see cref="CommandKind.SelectItem"/> and <see cref="CommandKind.ScrollTo"/>.
/// </summary>
readonly record struct Command(CommandKind Kind, int Index = -1)
{
    public static implicit operator Command(CommandKind kind) =>
        new(kind);

    public static Command Select(int index) =>
        new(CommandKind.SelectItem, index);

    public static Command Scroll(int top) =>
        new(CommandKind.ScrollTo, top);
}
