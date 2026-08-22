/// <summary>
/// Turns wheel messages into whole notches, keeping what is left over.
/// <para>
/// A wheel click sends 120, but a precision touchpad sends a fraction of that per message - ten to
/// sixty for an ordinary two finger movement. Dividing each message on its own and dropping the
/// remainder threw all of those away, so the canvas did not move at all under a touchpad while the
/// docked scrollbar, which accumulates internally, worked.
/// </para>
/// <para>
/// Its own type for the same reason <see cref="QueueTips" /> is: what matters is an accumulation
/// across messages, and that can be tested where a window cannot.
/// </para>
/// </summary>
sealed class WheelNotches(int perNotch)
{
    int remainder;

    /// <summary>
    /// The whole notches this message completes, which is usually none of one.
    /// </summary>
    public int Add(int delta)
    {
        remainder += delta;
        // Truncates toward zero, so a run of small movements one way accumulates and a movement
        // back the other way undoes it, rather than each direction keeping a debt of its own.
        var notches = remainder / perNotch;
        remainder -= notches * perNotch;
        return notches;
    }
}
