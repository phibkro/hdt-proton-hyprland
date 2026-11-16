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
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
public sealed record LaunchOptions(string? Replay, string Settings, string? ProtonState = null)
{
    public static LaunchOptions Current { get; set; } = new(null, DefaultSettings);
    private static string DefaultSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hdt-avalonia", "settings.json");
    public static LaunchOptions Parse(string[] args)
    {
        string? replay = null;
        string? protonState = null;
        var settings = DefaultSettings;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--replay" && i + 1 < args.Length) replay = args[++i];
            else if (args[i] == "--settings" && i + 1 < args.Length) settings = args[++i];
            else if (args[i] == "--proton-state" && i + 1 < args.Length) protonState = args[++i];
            else throw new ArgumentException("Usage: Hdt.Avalonia [--replay FILE] [--settings FILE] [--proton-state FILE]");
        }
        return new(replay, settings, protonState);
    }
}
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(LaunchOptions.Current);
        base.OnFrameworkInitializationCompleted();
    }
}
