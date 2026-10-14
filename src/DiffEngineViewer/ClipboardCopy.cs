/// <summary>
/// Hands the text waiting in <see cref="SessionState.Clipboard"/> to the window through the
/// kernel's <see cref="ClipboardPump"/>, and lets the state let go of it only once the window has
/// taken it or the pump has given up, which the status line then says.
/// </summary>
static class ClipboardCopy
{
    public static void Offer(ClipboardPump pump, SessionHost host, IViewerWindow window, PendingCopy copy)
    {
        switch (pump.Offer(copy, copy.Text, window.SetClipboard))
        {
            case ClipboardOffer.Taken:
                host.Mutate(_ => Release(_, copy));
                return;
            case ClipboardOffer.GaveUp:
                host.Mutate(_ => Failed(_, copy));
                return;
        }
    }

    /// <summary>
    /// Only the copy that was offered: one made since is still to be offered.
    /// </summary>
    static SessionState Release(SessionState state, PendingCopy copy)
    {
        if (!ReferenceEquals(state.Clipboard, copy))
        {
            return state;
        }

        return state with { Clipboard = null };
    }

    static SessionState Failed(SessionState state, PendingCopy copy)
    {
        if (!ReferenceEquals(state.Clipboard, copy))
        {
            return state;
        }

        return state with
        {
            Clipboard = null,
            Message = $"Could not copy {copy.What}: another program is holding the clipboard."
        };
    }
}
