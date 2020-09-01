using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Security.Permissions;

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
    Dictionary<int, Action> bindings = new Dictionary<int, Action>();

    public KeyRegister(IntPtr handle)
    {
        this.handle = handle;

        Application.AddMessageFilter(this);
    }

    public bool TryAddBinding(int id, KeyModifiers modifiers, Keys keys, Action action)
    {
        if (bindings.TryGetValue(id, out _))
        {
            UnregisterHotKey(IntPtr.Zero, id);
            bindings.Remove(id);
        }

        if (RegisterHotKey(handle, id, modifiers, keys))
        {
            bindings.Add(id, action);
            return true;
        }

        // If the operation failed, try to unregister the
        // HotKey if the thread has registered it before.

        // IntPtr.Zero means the HotKey registered by the thread.
        UnregisterHotKey(IntPtr.Zero, id);

        // Try to register the HotKey again.
        // If the operation still failed, it means that the HotKey
        // was already used in another thread or process.
        if (!RegisterHotKey(handle, id, modifiers, keys))
        {
            return false;
        }

        bindings.Add(id, action);
        return true;
    }

    [PermissionSetAttribute(SecurityAction.LinkDemand, Name = "FullTrust")]
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
        var id = (int)message.WParam;
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