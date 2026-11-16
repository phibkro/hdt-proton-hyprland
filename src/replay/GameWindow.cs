// Window-only stand-in for the isolated replay. No game logic or account access.
using System;
using System.IO;
using System.Runtime.InteropServices;
sealed class GameWindow
{
    delegate IntPtr Procedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WindowClass { public uint style; public Procedure procedure; public int classExtra, windowExtra; public IntPtr instance, icon, cursor, background; public string menuName, className; }
    [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; public uint extra; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out Message value, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message value);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message value);
    static Procedure procedure = Handle;
    static string output;
    static int clicks;
    static void Save()
    {
        File.WriteAllText(output + ".next", "{\"gameClicks\":" + clicks + "}");
        if (File.Exists(output)) File.Replace(output + ".next", output, null); else File.Move(output + ".next", output);
    }
    static IntPtr Handle(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x201) { clicks++; Save(); }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) return 2; output = args[0];
        var cls = new WindowClass { procedure = procedure, className = "UnityWndClass" };
        if (RegisterClass(ref cls) == 0) return 3;
        if (CreateWindowEx(0, "UnityWndClass", "Hearthstone", 0x90000000u, 1250, 120, 1000, 750, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero) return 4;
        Save(); Message msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        return 0;
    }
}
