using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Hdt.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            LaunchOptions.Current = LaunchOptions.Parse(args);
            if (LaunchOptions.Current.SessionRole == NativeUiRole.Overlay) return IndependentOverlay.Run(LaunchOptions.Current);
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
public sealed record LaunchOptions(string? Replay, string Settings, string? ProtonState = null, string? ClientData = null, string? NativeState = null, NativeUiRole? SessionRole = null, string? SessionInstance = null, GameIdentity? Game = null)
{
    public static LaunchOptions Current { get; set; } = new(null, DefaultSettings);
    private static string DefaultSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hdt-avalonia", "settings.json");
    public static LaunchOptions Parse(string[] args)
    {
        string? replay = null;
        string? protonState = null;
        string? clientData = null;
        string? nativeState = null;
        var settings = DefaultSettings;
        NativeUiRole? role = null; string? instance = null, gameClass = null; uint gameWindow = 0, gamePid = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--session-role" && i + 1 < args.Length) role = args[++i] switch { "main" => NativeUiRole.Main, "overlay" => NativeUiRole.Overlay, _ => throw new ArgumentException("Unknown explicit native role.") };
            else if (args[i] == "--session-instance" && i + 1 < args.Length) instance = args[++i];
            else if (args[i] == "--game-window" && i + 1 < args.Length) gameWindow = uint.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--game-pid" && i + 1 < args.Length) gamePid = uint.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--game-class" && i + 1 < args.Length) gameClass = args[++i];
            else if (args[i] == "--replay" && i + 1 < args.Length) replay = args[++i];
            else if (args[i] == "--settings" && i + 1 < args.Length) settings = args[++i];
            else if (args[i] == "--proton-state" && i + 1 < args.Length) protonState = args[++i];
            else if (args[i] == "--native-state" && i + 1 < args.Length) nativeState = args[++i];
            else if (args[i] == "--client-data" && i + 1 < args.Length) clientData = args[++i];
            else throw new ArgumentException("Usage: Hdt.Avalonia [--replay FILE] [--settings FILE] [--proton-state FILE] [--client-data FILE] [--native-state FILE]");
        }
        if (replay is not null && nativeState is not null) throw new ArgumentException("Choose replay acquisition or native observation acquisition explicitly.");
        if (role is not null && (nativeState is null || replay is not null || !Guid.TryParse(instance, out _))) throw new ArgumentException("Independent role requires explicit source and session instance.");
        if (role == NativeUiRole.Overlay && (gameWindow == 0 || gamePid == 0 || string.IsNullOrWhiteSpace(gameClass) || gameClass.Length > 128)) throw new ArgumentException("Independent overlay requires explicit game identity.");
        if (role is null && (instance is not null || gameClass is not null || gameWindow != 0 || gamePid != 0)) throw new ArgumentException("Session arguments require an explicit independent role.");
        return new(replay, settings, protonState, clientData, nativeState, role, instance, role == NativeUiRole.Overlay ? new(gameWindow, gamePid, gameClass!) : null);
    }
}
public class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(LaunchOptions.Current); desktop.MainWindow = window;
            if (LaunchOptions.Current.SessionRole == NativeUiRole.Main) SessionProtocol.AttachMain(window, LaunchOptions.Current, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
