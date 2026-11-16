// Isolated UI fixture host. Uses the installed HDT windows; no game client.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

sealed class ReplayHost
{
    static readonly string[] expectedScores = { "60", "61", "62", "80", "81", "82" };
    static string output;
    static string directory;
    static Window main, overlay;
    static Assembly hdt;
    static object arena;
    static int commandId, mouseDowns, exitCode;
    static bool finished;
    static string scene = "menu";
    static Popup popup;
    static object compatibilityPlugin;
    static CanonicalReplay canonical;
    static int pluginUpdates;
    static Button desktopControl;
    static int desktopClicks;
    static bool referenceSettings;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int WindowStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool MoveFileEx(string source, string destination, int flags);
    static int? plaqueLevel;
    static JavaScriptSerializer serializer = new JavaScriptSerializer();
    static void Record(string phase, Exception error)
    {
        for (var current = error; current != null; current = current.InnerException)
            File.AppendAllText(output, phase + " " + current.GetType().FullName + " " + current.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " " + current.Message + Environment.NewLine + current.StackTrace + Environment.NewLine);
    }
    [STAThread]
    static int Main(string[] args)
    {
        var setup = new AppDomainSetup
        {
            ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
            ConfigurationFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HearthstoneDeckTracker.exe.config")
        };
        var domain = AppDomain.CreateDomain("HDT isolated replay", null, setup);
        var runner = (ReplayRunner)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(ReplayRunner).FullName);
        return runner.Run(args);
    }
    public static int Run(string[] args)
    {
        if (args.Length != 4) return 2;
        output = args[0];
        plaqueLevel = args[2] == "mixed" ? (int?)null : int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            directory = Path.GetDirectoryName(output);
            hdt = Assembly.LoadFrom("HearthstoneDeckTracker.exe");
            if (!bool.Parse(args[3]))
            {
                var emitter = hdt.GetType("Hearthstone_Deck_Tracker.Controls.Overlay.Arena.ParticleEmitter", true);
                EventManager.RegisterClassHandler(emitter, FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
                {
                    e.Handled = true;
                    ((FrameworkElement)sender).Visibility = Visibility.Collapsed;
                }));
            }
            Application.ResourceAssembly = hdt;
            var app = (Application)Activator.CreateInstance(hdt.GetType("Hearthstone_Deck_Tracker.App", true));
            app.GetType().GetMethod("InitializeComponent").Invoke(app, null);
            var startup = app.GetType().GetMethod("App_OnStartup", BindingFlags.Instance | BindingFlags.NonPublic);
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.UnhandledExceptionFilter += (sender, e) => Record("dispatcher", e.Exception);
            var core = hdt.GetType("Hearthstone_Deck_Tracker.Core", true);
            main = (Window)core.GetProperty("MainWindow").GetValue(null, null);
            main.Left = 20; main.Top = 20; main.Width = 1100; main.Height = 650;
            main.Show();
            overlay = (Window)core.GetProperty("Overlay").GetValue(null, null);
            overlay.Left = 1250; overlay.Top = 120; overlay.Width = 1000; overlay.Height = 750;
            overlay.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            overlay.GetType().GetMethod("ShowOverlay").Invoke(overlay, new object[] { true });
            arena = Get(overlay, "ArenaPickHelperViewModel");
            if (bool.Parse(args[1]))
            {
                var assembly = Assembly.LoadFrom("ProtonCompatibility.dll");
                compatibilityPlugin = Activator.CreateInstance(assembly.GetType("ProtonCompatibility", true));
                compatibilityPlugin.GetType().GetMethod("OnLoad").Invoke(compatibilityPlugin, null);
            }
            main.AddHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent, new System.Windows.Input.MouseButtonEventHandler((sender, e) => mouseDowns++), true);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            bool firstTick = true;
            timer.Tick += (sender, e) =>
            {
                try
                {
                    if (firstTick) File.AppendAllText(output, "first-tick" + Environment.NewLine);
                    ProcessCommand();
                    if (finished) return;
                    if (compatibilityPlugin != null)
                    {
                        compatibilityPlugin.GetType().GetMethod("OnUpdate").Invoke(compatibilityPlugin, null);
                        pluginUpdates++;
                    }
                    Snapshot();
                    if (firstTick) File.AppendAllText(output, "first-snapshot" + Environment.NewLine);
                    firstTick = false;
                }
                catch (Exception error) { Record("command", error); exitCode = 1; Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            };
            timer.Start();
            File.AppendAllText(output, "ready main=" + main.GetType().FullName + " overlay=" + overlay.GetType().FullName + Environment.NewLine);
            // Use the normal WPF pump, with HDT's Core.Initialize startup handler
            // removed. Constructor side effects still run inside network isolation.
            app.Run();
            return exitCode;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Record("startup", error); return 1;
        }
    }
    static object Get(object target, string name)
    {
        var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) return p.GetValue(target, null);
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) throw new MissingMemberException(target.GetType().FullName, name);
        return f.GetValue(target);
    }
    static void Set(object target, string name, object value)
    {
        var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) { p.SetValue(target, value, null); return; }
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) throw new MissingMemberException(target.GetType().FullName, name);
        f.SetValue(target, value);
    }
    static void SetEnum(object target, string name, string value)
    {
        var property = target.GetType().GetProperty(name);
        if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
        property.SetValue(target, Enum.Parse(property.PropertyType, value), null);
    }
    static object EnsureChild(object target, string name)
    {
        var value = Get(target, name);
        if (value != null) return value;
        var p = target.GetType().GetProperty(name);
        var f = target.GetType().GetField(name);
        var type = p != null ? p.PropertyType : f.FieldType;
        value = Activator.CreateInstance(type); Set(target, name, value); return value;
    }
    static object MakeCard(Type cardType, string id, int index)
    {
        var ctor = cardType.GetConstructors().Single(c => c.GetParameters().Length == 5);
        var data = Activator.CreateInstance(ctor.GetParameters()[1].ParameterType);
        var scores = EnsureChild(data, "Arenasmith");
        Set(scores, "Score", (scene == "draft-1" ? 60 + index : 80 + index).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(scores, "Plaque", plaqueLevel ?? index * 2 + 1);
        // The installed model initializes dynamic scores with a level-zero
        // plaque. Its constructor prefers that plaque over the static score.
        var dynamicScores = EnsureChild(data, "ArenasmithDyn");
        Set(dynamicScores, "Score", (scene == "draft-1" ? 60 + index : 80 + index).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(dynamicScores, "Plaque", plaqueLevel ?? index * 2 + 1);
        EnsureChild(data, "Messages");
        var related = EnsureChild(data, "RelatedCards");
        var generated = EnsureChild(related, "GeneratedCardIds");
        EnsureChild(generated, "Generated");
        return ctor.Invoke(new object[] { id, data, new List<string>(), true, false });
    }
    static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T) yield return (T)child;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    static void SetScene(string value)
    {
        scene = value;
        var game = hdt.GetType("Hearthstone_Deck_Tracker.Core").GetProperty("Game").GetValue(null, null);
        Set(game, "IsRunning", true); Set(game, "IsInMenu", true);
        SetEnum(game, "CurrentMode", value == "menu" ? "HUB" : "DRAFT");
        var sceneProperty = hdt.GetType("Hearthstone_Deck_Tracker.SceneHandler").GetProperty("Scene");
        sceneProperty.SetValue(null, Get(game, "CurrentMode"), null);
        bool drafting = value.StartsWith("draft-", StringComparison.Ordinal);
        SetEnum(arena, "ClientState", drafting ? "Underground_Draft" : value == "menu" ? "None" : "Underground_Landing");
        SetEnum(arena, "SessionState", drafting ? "DRAFTING" : "INVALID");
        Set(arena, "IsDrafting", drafting); Set(arena, "IsAnimating", false);
        Set(arena, "IsArenasmithAvailable", drafting); Set(arena, "IsOverlayVisible", drafting);
        if (drafting)
        {
            var cardType = hdt.GetType("Hearthstone_Deck_Tracker.Controls.Overlay.Arena.ArenaPickSingleCardOptionViewModel", true);
            var property = arena.GetType().GetProperty("CardStats");
            var cards = (IList)Activator.CreateInstance(property.PropertyType);
            var ids = value == "draft-1" ? new[] { "CS2_029", "CS2_032", "CS2_172" } : new[] { "CS2_024", "CS2_025", "CS2_182" };
            for (int i = 0; i < ids.Length; i++) cards.Add(MakeCard(cardType, ids[i], i));
            property.SetValue(arena, cards, null);
            SetEnum(arena, "StateChange", "FadeIn");
        }
        overlay.GetType().GetMethod("UpdateArenaPickHelperVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overlay, null);
    }
    static void ProcessCommand()
    {
        var path = Path.Combine(directory, "command.json");
        if (!File.Exists(path)) return;
        var command = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        int id = Convert.ToInt32(command["id"], System.Globalization.CultureInfo.InvariantCulture);
        if (id <= commandId) return;
        string op = (string)command["op"];
        switch (op)
        {
            case "reference-options-index":
                var advanced = hdt.GetType("Hearthstone_Deck_Tracker.FlyoutControls.Options.AdvancedOptions", true);
                Set(advanced.GetProperty("Instance").GetValue(null, null), "Revealed", true);
                var pages = ReferencePages().Select(page => new { name = page.Name, title = Convert.ToString(page.Header) }).ToArray();
                if (pages.Length == 0) throw new InvalidOperationException("No original HDT settings pages found");
                File.WriteAllText(Path.Combine(directory, "reference-options-index.json"), serializer.Serialize(pages));
                referenceSettings = true;
                break;
            case "reference-options-page":
                var selectedPage = ReferencePages().Single(page => page.Name == (string)command["page"]);
                var options = (FrameworkElement)main.FindName("Options");
                Set(main.FindName("FlyoutOptions"), "IsOpen", true);
                options.GetType().GetMethod("ShowPage").Invoke(options, new object[] { selectedPage });
                ReferenceScroll().ScrollToTop();
                break;
            case "reference-options-scroll":
                var selected = (TreeViewItem)ReferenceTree().SelectedItem;
                if (selected == null || selected.Name != (string)command["page"])
                    throw new InvalidOperationException("Reference scroll page identity mismatch");
                var offset = Convert.ToDouble(command["offset"], System.Globalization.CultureInfo.InvariantCulture);
                var scroll = ReferenceScroll();
                if (offset < 0 || offset > scroll.ScrollableHeight)
                    throw new InvalidOperationException("Reference scroll is outside the original page");
                scroll.ScrollToVerticalOffset(offset);
                break;
            case "canonical-start":
                if (canonical != null) throw new InvalidOperationException("Canonical replay already initialized");
                canonical = new CanonicalReplay(hdt, directory); break;
            case "canonical-input":
                if (canonical == null) throw new InvalidOperationException("Canonical replay not initialized");
                var lines = serializer.DeserializeObject(File.ReadAllText(Path.Combine(directory, "canonical-input.json"))) as object[];
                foreach (System.Collections.Generic.Dictionary<string, object> line in lines)
                    canonical.Feed((string)line["ns"], (string)line["line"]);
                break;
            case "menu": case "arena-lobby": case "draft-1": case "draft-2": SetScene(op); break;
            case "settings-close": Set(main.FindName("FlyoutOptions"), "IsOpen", false); break;
            case "bgs-start":
                scene = "battlegrounds";
                var minions = Get(overlay, "BattlegroundsMinionsVM");
                Set(minions, "ActiveTier", (int?)1);
                File.AppendAllText(output, "bgs-tier-buttons=" + ((IEnumerable)Get(minions, "TierButtons")).Cast<object>().Count() +
                    " groups=" + ((IEnumerable)Get(minions, "Groups")).Cast<object>().Count() + Environment.NewLine);
                ((FrameworkElement)overlay.FindName("BgsTopBar")).Visibility = Visibility.Visible;
                ((FrameworkElement)overlay.FindName("GuidesTabs")).Visibility = Visibility.Visible;
                ((FrameworkElement)((UserControl)overlay.FindName("GuidesTabs")).FindName("TabsContent")).MaxHeight = overlay.ActualHeight - 54;
                break;
            case "bgs-minions-toggle":
                var tabs = Get(overlay, "BattlegroundsGuidesTabsViewModel");
                var minionsCommand = (System.Windows.Input.ICommand)Get(tabs, "ShowMinionsCommand");
                if (!minionsCommand.CanExecute(null)) throw new InvalidOperationException("Battlegrounds minion tab unavailable");
                var minionsButton = Children<FrameworkElement>(overlay).FirstOrDefault(b =>
                {
                    if (b.GetType().Name != "OverlayButton") return false;
                    var commandProperty = (DependencyProperty)b.GetType().GetField("CommandProperty").GetValue(null);
                    var binding = BindingOperations.GetBindingExpression(b, commandProperty);
                    return binding != null && binding.ParentBinding.Path.Path == "ShowMinionsCommand";
                });
                if (minionsButton == null) throw new InvalidOperationException("Battlegrounds card-back button missing");
                // Exercise WPF's routed observation separately from the command.
                // This is synthetic WPF input, not a native desktop-input test.
                minionsButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                ((ICommand)Get(minionsButton, "Command")).Execute(null);
                break;
            case "diagnostic-ui-stall":
                // Only the account-free replay host exposes this deliberate stall.
                System.Threading.Thread.Sleep(3500);
                break;
            case "bgs-end":
                Set(Get(overlay, "BattlegroundsGuidesTabsViewModel"), "ActiveViewModel", null);
                ((FrameworkElement)overlay.FindName("BgsTopBar")).Visibility = Visibility.Collapsed;
                scene = "menu";
                break;
            case "desktop-control":
                if (desktopControl == null)
                {
                    desktopControl = new Button
                    {
                        Content = "Portable input test",
                        Width = 160,
                        Height = 40,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    desktopControl.Click += (sender, e) => desktopClicks++;
                    hdt.GetType("Hearthstone_Deck_Tracker.Utility.Extensions.OverlayExtensions").GetMethod("SetIsOverlayHitTestVisible")
                        .Invoke(null, new object[] { desktopControl, true });
                    Panel.SetZIndex(desktopControl, Int32.MaxValue);
                    Canvas.SetLeft(desktopControl, 20); Canvas.SetTop(desktopControl, 20);
                    // HDT's own hover detector transforms registered controls to
                    // CanvasInfo; controls outside it never become clickable.
                    ((Panel)overlay.FindName("CanvasInfo")).Children.Add(desktopControl);
                }
                desktopControl.Visibility = Visibility.Visible; break;
            case "desktop-control-hide": if (desktopControl != null) desktopControl.Visibility = Visibility.Collapsed; break;
            case "popup-show":
                popup = new Popup
                {
                    PlacementTarget = main,
                    Placement = PlacementMode.Center,
                    AllowsTransparency = true,
                    Child = new Border { Background = Brushes.DarkBlue, Padding = new Thickness(20), Child = new TextBlock { Text = "Replay popup fixture", Foreground = Brushes.White } }
                };
                popup.IsOpen = true; break;
            case "popup-hide": if (popup != null) popup.IsOpen = false; break;
            case "new":
                var menu = main.FindName("MainWindowMenu") as FrameworkElement;
                var newDeckCommand = Get(menu.DataContext, "NewDeckCommand") as System.Windows.Input.ICommand;
                if (newDeckCommand == null || !newDeckCommand.CanExecute("Mage")) throw new InvalidOperationException("New deck command unavailable");
                newDeckCommand.Execute("Mage"); break;
            case "finish":
                commandId = id; Snapshot(); finished = true;
                if (compatibilityPlugin != null) compatibilityPlugin.GetType().GetMethod("OnUnload").Invoke(compatibilityPlugin, null);
                hdt.GetType("Hearthstone_Deck_Tracker.Core").GetProperty("IsShuttingDown").SetValue(null, true, null);
                Application.Current.Shutdown(exitCode); return;
            default: throw new InvalidOperationException("Unknown replay command");
        }
        commandId = id;
    }
    static TreeView ReferenceTree()
    {
        return (TreeView)((FrameworkElement)main.FindName("Options")).FindName("TreeViewOptions");
    }
    static IEnumerable<TreeViewItem> ReferencePages()
    {
        return ReferenceTree().Items.Cast<TreeViewItem>().SelectMany(category => category.Items.Cast<TreeViewItem>());
    }
    static ScrollViewer ReferenceScroll()
    {
        return (ScrollViewer)((FrameworkElement)main.FindName("Options")).FindName("ScrollViewerContent");
    }
    static object ReferenceState()
    {
        var options = (FrameworkElement)main.FindName("Options");
        var content = Get(options, "OptionsContent") as FrameworkElement;
        var page = ReferenceTree().SelectedItem as TreeViewItem;
        var scroll = ReferenceScroll();
        return new
        {
            page = page == null ? null : page.Name,
            title = page == null ? null : Convert.ToString(page.Header),
            contentType = content == null ? null : content.GetType().FullName,
            visible = content != null && content.IsVisible,
            width = content == null ? 0 : content.ActualWidth,
            height = content == null ? 0 : content.ActualHeight,
            offset = scroll.VerticalOffset,
            scrollableHeight = scroll.ScrollableHeight,
            viewportHeight = scroll.ViewportHeight
        };
    }
    static void Snapshot()
    {
        main.UpdateLayout();
        var button = Children<Button>(main).FirstOrDefault(b => b.IsVisible && Children<TextBlock>(b).Any(t => t.Text.Equals("settings", StringComparison.OrdinalIgnoreCase)));
        object bounds = null;
        if (button != null)
        {
            var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            bounds = new { x = (int)point.X, y = (int)point.Y };
        }
        var view = overlay.FindName("ArenaPickHelper") as FrameworkElement;
        var cards = Get(arena, "CardStats") as IEnumerable;
        var ids = new List<string>();
        var plaqueLevels = new List<int>();
        if (cards != null) foreach (var card in cards)
        {
            ids.Add((string)Get(Get(card, "Card"), "Id"));
            plaqueLevels.Add((int)Get(Get(card, "PlaqueViewModel"), "Level"));
        }
        object desktopBounds = null;
        if (desktopControl != null && desktopControl.IsVisible)
        {
            var point = desktopControl.PointToScreen(new Point(desktopControl.ActualWidth / 2, desktopControl.ActualHeight / 2));
            desktopBounds = new { x = (int)point.X, y = (int)point.Y };
        }
        var bgsContent = (FrameworkElement)((UserControl)overlay.FindName("GuidesTabs")).FindName("TabsContent");
        var data = new
        {
            observedUTC = DateTime.UtcNow.ToString("o"),
            commandId,
            scene,
            mouseDowns,
            pluginUpdates,
            desktopClicks,
            desktopControl = desktopBounds,
            overlayHwnd = new WindowInteropHelper(overlay).Handle.ToInt64(),
            foregroundHwnd = GetForegroundWindow().ToInt64(),
            overlayExtendedStyle = WindowStyle(new WindowInteropHelper(overlay).Handle, -20),
            clickthrough = Get(overlay, "_clickthrough"),
            battlegroundsContent = new
            {
                visible = bgsContent.IsVisible,
                width = bgsContent.ActualWidth,
                height = bgsContent.ActualHeight,
                minionViews = Children<FrameworkElement>(bgsContent).Count(e => e.IsVisible && e.GetType().Name == "BattlegroundsMinions"),
                groupViews = Children<FrameworkElement>(bgsContent).Count(e => e.IsVisible && e.GetType().Name == "BattlegroundsCardsGroup")
            },
            battlegroundsActiveTab = Get(Get(overlay, "BattlegroundsGuidesTabsViewModel"), "ActiveViewModel") == null ? null : Get(Get(overlay, "BattlegroundsGuidesTabsViewModel"), "ActiveViewModel").GetType().Name,
            mainWin32Handle = new WindowInteropHelper(main).Handle.ToInt64(),
            mainWidth = main.ActualWidth,
            mainHeight = main.ActualHeight,
            settingsButton = bounds,
            settingsOpen = (bool)Get(main.FindName("FlyoutOptions"), "IsOpen"),
            settingsReference = referenceSettings ? ReferenceState() : null,
            arenaVisibility = view.Visibility.ToString(),
            arenaOpacity = view.Opacity,
            cards = ids,
            plaqueLevels,
            renderingTier = RenderCapability.Tier,
            visibleEmitters = Children<FrameworkElement>(overlay).Count(e => e.IsVisible && e.GetType().Name == "ParticleEmitter"),
            popupOpen = popup != null && popup.IsOpen,
            visibleDialogTypes = Children<FrameworkElement>(main).Where(e => e.IsVisible && e.GetType().Name.EndsWith("Dialog", StringComparison.Ordinal)).Select(e => e.GetType().FullName).Distinct().ToArray(),
            visibleScores = Children<FrameworkElement>(overlay).Where(t => t.IsVisible && t.GetType().Name == "HearthstoneTextBlock")
                .Select(t => (string)Get(t, "Text")).Where(t => expectedScores.Contains(t)).Distinct().OrderBy(t => t).ToArray()
        };
        var path = Path.Combine(directory, "state.json");
        File.WriteAllText(path + ".next", serializer.Serialize(data));
        if (!MoveFileEx(path + ".next", path, 1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}

public class ReplayRunner : MarshalByRefObject
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Reflection creates this MarshalByRefObject in the isolated AppDomain and invokes its instance Run entry point.")]
    public int Run(string[] args) { return ReplayHost.Run(args); }
}
