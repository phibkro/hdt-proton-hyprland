// Experimental X11-only handshake. The native owner controls input regions;
// this adapter keeps Wine activation and HDT's cached click-through state aligned.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using Hearthstone_Deck_Tracker.Utility.Logging;

sealed class PortableOverlayInput
{
    const int NoActivate = 0x08000000;
    const int AppWindow = 0x00040000;
    const int OwnedMask = NoActivate | AppWindow;
    const uint RefreshWithoutActivation = 0x0037;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int ExtendedStyle(IntPtr window, int index);

    public sealed class Acknowledgement
    {
        public string session { get; set; }
        public string backend { get; set; }
        public bool nativeReady { get; set; }
        public bool fresh { get; set; }
        public bool active { get; set; }
        public bool focused { get; set; }
        public bool pointerOverControl { get; set; }
        public long overlayHwnd { get; set; }
        public long observedMS { get; set; }
    }

    IntPtr window;
    int originalBits;
    int appliedBits;
    bool stylesOwned;
    bool focusOwned;
    bool disabled;
    MethodInfo setClickthrough;

    static Acknowledgement Read(string path, string session, IntPtr hwnd)
    {
        try
        {
            if (new FileInfo(path).Length > 8192) return null;
            var ack = new JavaScriptSerializer().Deserialize<Acknowledgement>(File.ReadAllText(path));
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return ack != null && ack.session == session && ack.backend == "x11" && ack.nativeReady &&
                ack.overlayHwnd == hwnd.ToInt64() && ack.observedMS > now - 1500 && ack.observedMS < now + 1500 ? ack : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public void Update(Window overlay)
    {
        if (disabled) return;
        var input = Environment.GetEnvironmentVariable("HDT_INPUT_REGION_FILE");
        var session = Environment.GetEnvironmentVariable("HDT_INPUT_SESSION");
        var hwnd = new WindowInteropHelper(overlay).Handle;
        if (String.IsNullOrEmpty(input) || String.IsNullOrEmpty(session) || hwnd == IntPtr.Zero) return;
        var ack = Read(Path.Combine(Path.GetDirectoryName(input), "control-input.json"), session, hwnd);
        if (ack == null) { Stop(); return; }
        if (window != hwnd) { Stop(); window = hwnd; }
        try
        {
            if (!stylesOwned)
            {
                setClickthrough = overlay.GetType().GetMethod("SetClickthrough", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
                if (setClickthrough == null) throw new MissingMethodException("HDT SetClickthrough(bool) unavailable");
                originalBits = ExtendedStyle(hwnd, -20) & OwnedMask;
                appliedBits = originalBits;
                stylesOwned = true;
                // Native readiness means the owner has already installed empty
                // input shaping. Manage the popup before interactive activation.
                Hearthstone_Deck_Tracker.User32.SetWindowExStyle(hwnd, AppWindow);
                appliedBits = ExtendedStyle(hwnd, -20) & OwnedMask;
                if (!Hearthstone_Deck_Tracker.User32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, RefreshWithoutActivation))
                    throw new InvalidOperationException("Portable popup management request failed");
                Hearthstone_Deck_Tracker.User32.RemoveWindowExStyle(hwnd, NoActivate);
                appliedBits = ExtendedStyle(hwnd, -20) & OwnedMask;
                if (appliedBits != AppWindow) throw new InvalidOperationException("Portable activation styles were not applied");
            }
            if (ack.fresh && ack.active && ack.pointerOverControl)
                setClickthrough.Invoke(overlay, new object[] { false });
            var foreground = Hearthstone_Deck_Tracker.User32.GetForegroundWindow();
            if (ack.fresh && ack.active && ack.focused && ack.pointerOverControl && foreground != hwnd)
                focusOwned = overlay.Activate();
            else if (focusOwned && ack.active && !ack.pointerOverControl && foreground == hwnd)
            {
                var game = Hearthstone_Deck_Tracker.User32.GetHearthstoneWindow();
                if (game != IntPtr.Zero) Hearthstone_Deck_Tracker.User32.SetForegroundWindow(game);
                focusOwned = false;
            }
            else if (!ack.active) focusOwned = false;
        }
        catch (Exception error)
        {
            // Disable only this optional input path. Never open a modal crash
            // dialog or install a replacement global exception handler.
            Log.Error("Portable X11 input disabled: " + error.GetType().Name);
            Stop();
            disabled = true;
        }
    }

    public void Stop()
    {
        if (focusOwned && window != IntPtr.Zero && Hearthstone_Deck_Tracker.User32.GetForegroundWindow() == window)
        {
            var game = Hearthstone_Deck_Tracker.User32.GetHearthstoneWindow();
            if (game != IntPtr.Zero) Hearthstone_Deck_Tracker.User32.SetForegroundWindow(game);
        }
        if (stylesOwned && window != IntPtr.Zero)
        {
            var unchanged = ~(ExtendedStyle(window, -20) ^ appliedBits) & OwnedMask;
            Hearthstone_Deck_Tracker.User32.RemoveWindowExStyle(window, appliedBits & ~originalBits & unchanged);
            Hearthstone_Deck_Tracker.User32.SetWindowExStyle(window, originalBits & ~appliedBits & unchanged);
        }
        stylesOwned = false;
        focusOwned = false;
        window = IntPtr.Zero;
    }
}
