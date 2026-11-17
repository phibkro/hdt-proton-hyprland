using Avalonia;
using AvaloniaUI.DiagnosticsSupport;
using Hdt.Avalonia;

try
{
    LaunchOptions.Current = LaunchOptions.Parse(args);
    if (LaunchOptions.Current.SessionRole is not null)
        throw new ArgumentException("Use the ordinary application for independently owned session roles.");
    return AppBuilder.Configure<DevelopmentApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

internal sealed class DevelopmentApp : App
{
    public override void Initialize()
    {
        base.Initialize();
        this.AttachDeveloperTools(options =>
        {
            options.ApplicationName = "HDT native migration preview";
            options.ConnectOnStartup = false;
            // The project-local tool is started explicitly, never a global avdt.
            options.Runner = DeveloperToolsRunner.NoOp;
        });
    }
}
