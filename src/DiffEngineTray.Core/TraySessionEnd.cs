static class TraySessionEnd
{
    /// <summary>
    /// What a clean exit does on its way out, for a session that ends instead: the queue staged,
    /// when it is held here, and the version marker removed. Each head hears the session end its
    /// own way, and hands this to whatever does.
    /// <para>
    /// The queue only where this tray owns it, since a viewer that owns it stages its own. The
    /// marker whoever owns the queue: it says a tray of this version is running, to whatever
    /// reads it to decide what it may send, and left behind by a logoff it went on saying so
    /// until a tray was next started, which after a logoff need not be this version or any.
    /// </para>
    /// <para>
    /// The queue first, being the one that cannot be had again, and the marker whether or not
    /// that worked.
    /// </para>
    /// </summary>
    /// <param name="removeMarker"><see cref="TrayVersionFile.Delete"/>, except in a test.</param>
    public static Action For(OwnedInlineHost? owned, Action removeMarker) =>
        () =>
        {
            try
            {
                owned?.SessionEnding();
            }
            finally
            {
                removeMarker();
            }
        };
}
