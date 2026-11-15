// Read-only Win32 geometry probe. No hooks, focus, window messages, or game memory reads.
using System;
using System.IO;
using System.Runtime.InteropServices;
class WindowProbe {
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x, y; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int n);
    static string Inspect(string cls, string title) {
        IntPtr h = FindWindow(cls, title);
        if(h == IntPtr.Zero) return "null";
        Rect w, c;
        Point origin = new Point();
        if(!GetWindowRect(h, out w) || !GetClientRect(h, out c) || !ClientToScreen(h, ref origin))
            throw new Exception("Window geometry API failed");
        return String.Format("{{\"window\":[{0},{1},{2},{3}],\"clientOrigin\":[{4},{5}],\"clientSize\":[{6},{7}],\"exStyle\":\"0x{8:x}\"}}",
            w.left, w.top, w.right-w.left, w.bottom-w.top, origin.x, origin.y, c.right-c.left, c.bottom-c.top, GetWindowLongPtr(h,-20).ToInt64());
    }
    static int Main(string[] args) {
        try {
            File.WriteAllText(args[0], "{\"observedUTC\":\""+DateTime.UtcNow.ToString("o")+"\",\"game\":"+Inspect("UnityWndClass","Hearthstone")+",\"overlay\":"+Inspect(null,"HearthstoneOverlay")+"}");
            return 0;
        } catch { return 1; }
    }
}
