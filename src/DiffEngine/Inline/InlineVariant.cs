namespace DiffEngine;

/// <summary>
/// One distinct content for a call site. <paramref name="Origins"/> holds the framework labels
/// that produced this exact content ("net8.0"); empty means an unlabeled sender. Two frameworks
/// producing identical content share one variant carrying both labels, which is why an entry
/// holding more than one variant is a real disagreement rather than a duplicate.
/// </summary>
public sealed record InlineVariant(InlinePatch Patch, IReadOnlyList<string> Origins)
{
    /// <summary>
    /// The origins joined for display: "net8.0, net9.0". Null when unlabeled.
    /// </summary>
    public string? Label => Origins.Count == 0 ? null : string.Join(", ", Origins);
}
