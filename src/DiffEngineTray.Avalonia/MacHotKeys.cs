using System.Runtime.InteropServices;

namespace DiffEngineTray;

// Best-effort macOS global hotkeys via the Carbon RegisterEventHotKey API (the cross-app
// equivalent of the Windows RegisterHotKey used by KeyRegister).
//
// This cannot be exercised from Windows or from headless CI (global hotkeys require a logged-in
// GUI session), so every entry point is guarded to fail safe: any error is logged and the app
// keeps running without hotkeys. Needs validation on a real Mac.
class MacHotKeys :
    IDisposable
{
    const string carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [StructLayout(LayoutKind.Sequential)]
    struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int EventHandlerDelegate(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    [DllImport(carbon)]
    static extern IntPtr GetApplicationEventTarget();

    // ItemCount/ByteCount are `unsigned long` (64-bit on LP64 macOS) -> marshal as UIntPtr.
    [DllImport(carbon)]
    static extern int InstallEventHandler(IntPtr target, IntPtr handler, UIntPtr numTypes, EventTypeSpec[] typeList, IntPtr userData, out IntPtr handlerRef);

    [DllImport(carbon)]
    static extern int RegisterEventHotKey(uint hotKeyCode, uint hotKeyModifiers, EventHotKeyID hotKeyId, IntPtr target, uint options, out IntPtr hotKeyRef);

    [DllImport(carbon)]
    static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    [DllImport(carbon)]
    static extern int GetEventParameter(IntPtr eventRef, uint name, uint type, IntPtr outActualType, UIntPtr bufferSize, IntPtr outActualSize, out EventHotKeyID outData);

    const uint kEventClassKeyboard = 0x6b657962; // 'keyb'
    const uint kEventHotKeyPressed = 5; // kEventHotKeyReleased is 6
    const uint kEventParamDirectObject = 0x2d2d2d2d; // '----'
    const uint typeEventHotKeyID = 0x686b6964; // 'hkid'
    const uint signature = 0x44494646; // 'DIFF'

    // Carbon modifier masks.
    const uint shiftKey = 0x0200;
    const uint optionKey = 0x0800;
    const uint controlKey = 0x1000;

    static readonly Dictionary<uint, Action> actions = new();

    readonly EventHandlerDelegate handler;
    IntPtr handlerRef;
    readonly List<IntPtr> registered = new();

    MacHotKeys() =>
        // Keep the delegate rooted so the marshalled function pointer stays valid.
        handler = HandleHotKey;

    public static MacHotKeys? TryCreate(Settings settings, Tracker tracker)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            var hotKeys = new MacHotKeys();
            hotKeys.Install();
            hotKeys.Rebind(settings, tracker);
            return hotKeys;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to initialize macOS global hotkeys; continuing without them.");
            return null;
        }
    }

    void Install()
    {
        var typeList = new[]
        {
            new EventTypeSpec
            {
                EventClass = kEventClassKeyboard,
                EventKind = kEventHotKeyPressed
            }
        };
        var functionPointer = Marshal.GetFunctionPointerForDelegate(handler);
        var status = InstallEventHandler(GetApplicationEventTarget(), functionPointer, (UIntPtr) 1, typeList, IntPtr.Zero, out handlerRef);
        if (status != 0)
        {
            throw new($"InstallEventHandler failed: {status}");
        }
    }

    public void Rebind(Settings settings, Tracker tracker)
    {
        ClearRegistrations();
        actions.Clear();

        Register(settings.AcceptAllHotKey, KeyBindingIds.AcceptAll, tracker.AcceptAll);
        Register(settings.DiscardAllHotKey, KeyBindingIds.DiscardAll, tracker.Clear);
        Register(settings.AcceptOpenHotKey, KeyBindingIds.AcceptOpen, tracker.AcceptOpen);
    }

    void Register(HotKey? hotKey, int id, Action action)
    {
        if (hotKey == null)
        {
            return;
        }

        if (!CarbonKeyCodes.TryGet(hotKey.Key, out var keyCode))
        {
            Log.Warning("No macOS key code mapping for hotkey '{Key}'", hotKey.Key);
            return;
        }

        var modifiers = 0u;
        if (hotKey.Control)
        {
            modifiers |= controlKey;
        }

        if (hotKey.Alt)
        {
            modifiers |= optionKey;
        }

        if (hotKey.Shift)
        {
            modifiers |= shiftKey;
        }

        var hotKeyId = new EventHotKeyID
        {
            Signature = signature,
            Id = (uint) id
        };
        var status = RegisterEventHotKey(keyCode, modifiers, hotKeyId, GetApplicationEventTarget(), 0, out var reference);
        if (status != 0)
        {
            Log.Warning("RegisterEventHotKey failed for id {Id}: {Status}", id, status);
            return;
        }

        registered.Add(reference);
        actions[(uint) id] = action;
    }

    static int HandleHotKey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        try
        {
            var status = GetEventParameter(
                eventRef,
                kEventParamDirectObject,
                typeEventHotKeyID,
                IntPtr.Zero,
                (UIntPtr) Marshal.SizeOf<EventHotKeyID>(),
                IntPtr.Zero,
                out var hotKeyId);
            if (status == 0 &&
                actions.TryGetValue(hotKeyId.Id, out var action))
            {
                Dispatcher.UIThread.Post(action);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Error handling global hotkey");
        }

        return 0;
    }

    void ClearRegistrations()
    {
        foreach (var reference in registered)
        {
            UnregisterEventHotKey(reference);
        }

        registered.Clear();
    }

    public void Dispose()
    {
        ClearRegistrations();
        actions.Clear();
    }
}

static class CarbonKeyCodes
{
    // ANSI virtual key codes (kVK_ANSI_*), keyed by the Windows Forms Keys enum names that the
    // settings persist (e.g. "A", "D1", "F5").
    static readonly Dictionary<string, uint> map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0x00, ["B"] = 0x0B, ["C"] = 0x08, ["D"] = 0x02, ["E"] = 0x0E,
        ["F"] = 0x03, ["G"] = 0x05, ["H"] = 0x04, ["I"] = 0x22, ["J"] = 0x26,
        ["K"] = 0x28, ["L"] = 0x25, ["M"] = 0x2E, ["N"] = 0x2D, ["O"] = 0x1F,
        ["P"] = 0x23, ["Q"] = 0x0C, ["R"] = 0x0F, ["S"] = 0x01, ["T"] = 0x11,
        ["U"] = 0x20, ["V"] = 0x09, ["W"] = 0x0D, ["X"] = 0x07, ["Y"] = 0x10,
        ["Z"] = 0x06,
        ["D0"] = 0x1D, ["D1"] = 0x12, ["D2"] = 0x13, ["D3"] = 0x14, ["D4"] = 0x15,
        ["D5"] = 0x17, ["D6"] = 0x16, ["D7"] = 0x1A, ["D8"] = 0x1C, ["D9"] = 0x19,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60,
        ["F6"] = 0x61, ["F7"] = 0x62, ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D,
        ["F11"] = 0x67, ["F12"] = 0x6F
    };

    public static bool TryGet(string key, out uint code) =>
        map.TryGetValue(key, out code);
}
