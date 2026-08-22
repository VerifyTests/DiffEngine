//https://stackoverflow.com/a/35591706/53158
public class KeyRegister :
    IMessageFilter,
    IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr handle, int id, KeyModifiers modifiers, Keys vk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr handle, int id);

    IntPtr handle;
    Dictionary<int, Action> bindings = [];

    public KeyRegister(IntPtr handle)
    {
        this.handle = handle;

        Application.AddMessageFilter(this);
    }

    public bool TryAddBinding(int id, bool shift, bool control, bool alt, string key, Action action)
    {
        var modifiers = KeyModifiers.None;
        if (shift)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (control)
        {
            modifiers |= KeyModifiers.Control;
        }

        if (alt)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (!KeyName.TryParse(key, out var keys))
        {
            // Unbound rather than thrown. This runs at startup for every configured hot key, and
            // a hand edited settings.json used to take the tray down at every login
            Log.Error("'{Key}' is not a key name. The hot key was not bound.", key);
            return false;
        }

        return TryAddBinding(id, modifiers, keys, action);
    }

    public bool TryAddBinding(int id, KeyModifiers modifiers, Keys keys, Action action)
    {
        UnregisterHotKey(handle, id);

        if (!RegisterHotKey(handle, id, modifiers, keys))
        {
            return false;
        }

        bindings[id] = action;
        return true;
    }

    public void ClearBinding(int id)
    {
        bindings.Remove(id);
        UnregisterHotKey(handle, id);
    }

    public bool PreFilterMessage(ref Message message)
    {
        // false to allow the message to continue to the next filter
        const int WM_HOTKEY = 0x0312;
        if (message.Msg != WM_HOTKEY ||
            message.HWnd != handle)
        {
            return false;
        }

        // The property WParam of Message is typically used to store small pieces
        // of information. In this scenario, it stores the ID.
        var id = (int) message.WParam;
        if (!bindings.TryGetValue(id, out var action))
        {
            return false;
        }

        action();

        // true to filter message and stop it from being dispatched
        return true;
    }

    public void Dispose()
    {
        Application.RemoveMessageFilter(this);

        foreach (var id in bindings.Keys)
        {
            UnregisterHotKey(handle, id);
        }
    }
}