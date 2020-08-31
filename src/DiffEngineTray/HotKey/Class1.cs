using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Security.Permissions;

//https://stackoverflow.com/a/35591706/53158
public class HotKeyRegister :
    IMessageFilter,
    IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, KeyModifiers fsModifiers, Keys vk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    bool disposed;

    const int WM_HOTKEY = 0x0312;

    public IntPtr Handle { get; }

    public int ID { get; }

    public KeyModifiers Modifiers { get; }

    public Keys Key { get; }

    public event EventHandler HotKeyPressed = null!;

    public HotKeyRegister(IntPtr handle, int id, KeyModifiers modifiers, Keys key)
    {
        if (key == Keys.None || modifiers == KeyModifiers.None)
        {
            throw new ArgumentException("The key or modifiers could not be None.");
        }

        Handle = handle;
        ID = id;
        Modifiers = modifiers;
        Key = key;

        RegisterHotKey();
        Application.AddMessageFilter(this);
    }

    private void RegisterHotKey()
    {
        var isKeyRegistered = RegisterHotKey(Handle, ID, Modifiers, Key);

        // If the operation failed, try to unregister the
        // HotKey if the thread has registered it before.
        if (!isKeyRegistered)
        {
            // IntPtr.Zero means the HotKey registered by the thread.
            UnregisterHotKey(IntPtr.Zero, ID);

            // Try to register the HotKey again.
            isKeyRegistered = RegisterHotKey(Handle, ID, Modifiers, Key);

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
        // The property WParam of Message is typically used to store small pieces
        // of information. In this scenario, it stores the ID.
        if (m.Msg == WM_HOTKEY
            && m.HWnd == Handle
            && m.WParam == (IntPtr)ID
            && HotKeyPressed != null)
        {
            // Raise the HotKeyPressed event if it is an WM_HOTKEY message.
            HotKeyPressed(this, EventArgs.Empty);

            // True to filter the message and stop it from being dispatched.
            return true;
        }

        // Return false to allow the message to continue to the next filter or
        // control.
        return false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {

            Application.RemoveMessageFilter(this);

            UnregisterHotKey(Handle, ID);
        }

        disposed = true;
    }
}