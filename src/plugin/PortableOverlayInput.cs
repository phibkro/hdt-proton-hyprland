// Experimental X11-only handshake. The native owner controls input regions;
// this adapter keeps Wine activation and HDT's cached click-through state aligned.
using System;
using System.Collections;
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
    const int Transparent = 0x20;
    const int OwnedMask = NoActivate | AppWindow;
    const uint RefreshWithoutActivation = 0x0037;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int ExtendedStyle(IntPtr window, int index);

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    static extern short GetKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public sealed class Acknowledgement
    {
        public string session { get; set; }
        public string backend { get; set; }
        public bool nativeReady { get; set; }
        public bool fresh { get; set; }
        public bool active { get; set; }
        public bool focused { get; set; }
        public bool pointerOverControl { get; set; }
        public int inputControls { get; set; }
        public long overlayHwnd { get; set; }
        public long observedMS { get; set; }
    }

    IntPtr window;
    int originalBits;
    int appliedBits;
    bool stylesOwned;
    bool originalClickthrough;
    bool focusOwned;
    bool disabled;
    bool pointerAdmitted;
    IList clickableElements;
    FrameworkElement inputSurface;
    bool surfaceOwned;
    MethodInfo setClickthrough;

    public bool PreparedFor(IntPtr hwnd)
    {
        return !disabled && stylesOwned && window == hwnd &&
            (ExtendedStyle(hwnd, -20) & (OwnedMask | Transparent)) == AppWindow;
    }

    public string InputState(Window overlay)
    {
        var hwnd = new WindowInteropHelper(overlay).Handle;
        if (!PreparedFor(hwnd)) return "Unprepared";
        return "Prepared";
    }

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
        catch (Exception error)
        {
            // Framework's ObjectConverter wraps malformed numeric fields in
            // System.Exception. Invalid IPC must not escape onto the UI thread.
            Log.Warn("Rejected portable input acknowledgement: " + error.GetType().Name);
            return null;
        }
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
                var clickableField = overlay.GetType().GetField("_clickableElements", BindingFlags.Instance | BindingFlags.NonPublic);
                clickableElements = clickableField == null ? null : clickableField.GetValue(overlay) as IList;
                inputSurface = overlay.FindName("CanvasInfo") as FrameworkElement;
                if (clickableElements == null || inputSurface == null)
                    throw new MissingMemberException("HDT interactivity surface unavailable");
                // Lease only HDT's Win32 click-through decision. Do not mark the
                // surface as an exported control: native SHAPE remains the sole
                // owner of which pixels accept input. Otherwise the original
                // hover loop and this adapter repeatedly flip WS_EX_TRANSPARENT.
                if (!clickableElements.Contains(inputSurface))
                {
                    clickableElements.Add(inputSurface);
                    surfaceOwned = true;
                }
                originalBits = ExtendedStyle(hwnd, -20) & OwnedMask;
                originalClickthrough = (ExtendedStyle(hwnd, -20) & Transparent) != 0;
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
            // The native owner keeps input empty until this preparation is
            // published. Make Wine receptive before X11 delivers pointer entry.
            if (ack.active) setClickthrough.Invoke(overlay, new object[] { false });
            var foreground = Hearthstone_Deck_Tracker.User32.GetForegroundWindow();
            if (ack.fresh && ack.active && ack.focused && ack.pointerOverControl && (foreground != hwnd || !overlay.IsActive))
                focusOwned = overlay.Activate();
            else if (focusOwned && ack.active && !ack.pointerOverControl && foreground == hwnd)
            {
                var game = Hearthstone_Deck_Tracker.User32.GetHearthstoneWindow();
                if (game != IntPtr.Zero) Hearthstone_Deck_Tracker.User32.SetForegroundWindow(game);
                focusOwned = false;
            }
            else if (!ack.active) focusOwned = false;

            var admitted = ack.fresh && ack.active && ack.focused && ack.pointerOverControl &&
                ack.inputControls > 0 && overlay.IsActive &&
                Hearthstone_Deck_Tracker.User32.GetForegroundWindow() == hwnd;
            if (!admitted) pointerAdmitted = false;
            else if (!pointerAdmitted)
            {
                NativePoint point;
                // X11 can deliver EnterNotify after input shaping without Wine
                // producing WM_MOUSEMOVE. Refresh only our admitted window from
                // the real cursor; never move it, forward buttons or target game.
                if (GetCursorPos(out point) && WindowFromPoint(point) == hwnd &&
                    GetKeyState(1) >= 0 && GetKeyState(2) >= 0 && GetKeyState(4) >= 0 &&
                    GetKeyState(5) >= 0 && GetKeyState(6) >= 0 && ScreenToClient(hwnd, ref point) &&
                    point.X >= 0 && point.X <= short.MaxValue && point.Y >= 0 && point.Y <= short.MaxValue)
                {
                    var keys = (GetKeyState(16) < 0 ? 4 : 0) | (GetKeyState(17) < 0 ? 8 : 0);
                    if (!PostMessage(hwnd, 0x0200, new IntPtr(keys), new IntPtr(point.X | (point.Y << 16))))
                        throw new InvalidOperationException("Portable pointer refresh failed");
                    pointerAdmitted = true;
                }
            }
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
        if (surfaceOwned && clickableElements != null) clickableElements.Remove(inputSurface);
        surfaceOwned = false;
        clickableElements = null;
        inputSurface = null;
        if (focusOwned && window != IntPtr.Zero && Hearthstone_Deck_Tracker.User32.GetForegroundWindow() == window)
        {
            var game = Hearthstone_Deck_Tracker.User32.GetHearthstoneWindow();
            if (game != IntPtr.Zero) Hearthstone_Deck_Tracker.User32.SetForegroundWindow(game);
        }
        if (stylesOwned && window != IntPtr.Zero)
        {
            var overlay = Hearthstone_Deck_Tracker.API.Core.OverlayWindow;
            try
            {
                if (overlay != null && new WindowInteropHelper(overlay).Handle == window && setClickthrough != null)
                    setClickthrough.Invoke(overlay, new object[] { originalClickthrough });
            }
            catch (Exception error) { Log.Warn("Portable click-through restoration failed: " + error.GetType().Name); }
            var unchanged = ~(ExtendedStyle(window, -20) ^ appliedBits) & OwnedMask;
            Hearthstone_Deck_Tracker.User32.RemoveWindowExStyle(window, appliedBits & ~originalBits & unchanged);
            Hearthstone_Deck_Tracker.User32.SetWindowExStyle(window, originalBits & ~appliedBits & unchanged);
        }
        stylesOwned = false;
        pointerAdmitted = false;
        focusOwned = false;
        window = IntPtr.Zero;
    }
}
