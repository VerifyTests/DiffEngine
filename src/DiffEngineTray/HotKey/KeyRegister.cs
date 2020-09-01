using System;
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
    int id;
    KeyModifiers modifiers;
    Keys key;
    Action action;

    public KeyRegister(IntPtr handle, int id, KeyModifiers modifiers, Keys key, Action action)
    {
        if (key == Keys.None)
        {
            throw new ArgumentException("The key could not be None.");
        }

        this.handle = handle;
        this.id = id;
        this.modifiers = modifiers;
        this.key = key;
        this.action = action;

        RegisterHotKey();
        Application.AddMessageFilter(this);
    }

    void RegisterHotKey()
    {
        var isKeyRegistered = RegisterHotKey(handle, id, modifiers, key);

        // If the operation failed, try to unregister the
        // HotKey if the thread has registered it before.
        if (!isKeyRegistered)
        {
            // IntPtr.Zero means the HotKey registered by the thread.
            UnregisterHotKey(IntPtr.Zero, id);

            // Try to register the HotKey again.
            isKeyRegistered = RegisterHotKey(handle, id, modifiers, key);

            // If the operation still failed, it means that the HotKey
            // was already used in another thread or process.
            if (!isKeyRegistered)
            {
                throw new ApplicationException("The HotKey is in use");
            }
        }
    }

    [PermissionSetAttribute(SecurityAction.LinkDemand, Name = "FullTrust")]
    public bool PreFilterMessage(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        // The property WParam of Message is typically used to store small pieces
        // of information. In this scenario, it stores the ID.
        if (m.Msg == WM_HOTKEY
            && m.HWnd == handle
            && m.WParam == (IntPtr) id)
        {
            // Raise the HotKeyPressed event a WM_HOTKEY message.
            action();

            // true to filter the message and stop it from being dispatched
            return true;
        }

        // false to allow the message to continue to the next filter
        return false;
    }

    public void Dispose()
    {
        Application.RemoveMessageFilter(this);

        UnregisterHotKey(handle, id);
    }
}