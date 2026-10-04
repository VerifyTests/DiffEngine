namespace DiffEngine;

/// <summary>
/// What a viewer's exit code says to the process that started it.
/// <para>
/// Here, beside the wire, because it is the same kind of agreement between the same two parties:
/// <c>ViewerLaunchGate</c> reads what <c>ViewerProgram</c> returns, and the copy of the viewer
/// that resolves is not always the one this library was built beside. Neither is named as a
/// reference, since this file is compiled into both and each has only its own half.
/// </para>
/// </summary>
static class ViewerExit
{
    /// <summary>
    /// The viewer could not show what it was started with, and every inline snapshot it held is
    /// staged where accept tooling finds it (<see cref="InlineStaging.Persist" />). So whoever
    /// started it has nothing to stage itself.
    /// <para>
    /// A failure all the same, which is what makes it safe between versions. A viewer from before
    /// it never returns it, and its caller stages as it always did. A library from before it reads
    /// it as it reads any exit that is not zero, as a launch that failed, and stages a second trio
    /// beside the viewer's: what both did before there was a code for it. So no copy has to be
    /// passed over for this, and <c>ViewerContract</c> asks for nothing more.
    /// </para>
    /// <para>
    /// Only ever the answer to a launch with an inline patch. A viewer started for a delete or a
    /// pair that could not open its window has not dealt with what it was given by staging
    /// someone else's snapshots, and says it failed.
    /// </para>
    /// </summary>
    public const int Staged = 5;
}
