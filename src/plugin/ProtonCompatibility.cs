// Local HDT plugin, using HDT's existing plugin lifecycle and layout APIs.
// No game writes, injected game code, or synthetic input.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.ComponentModel;
using System.Windows.Data;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Api = Hearthstone_Deck_Tracker.API.Core;

public sealed class ProtonCompatibility : IPlugin
{
    public string Name { get { return "Proton desktop compatibility"; } }
    public string Description { get { return "Refresh overlay geometry, suppress incompatible Arena particles, and export input diagnostics."; } }
    public string ButtonText { get { return "No settings"; } }
    public string Author { get { return "Local investigation"; } }
    public Version Version { get { return new Version(0, 6, 0); } }
    public MenuItem MenuItem { get { return null; } }
    FlightRecorder recorder;
    INotifyPropertyChanged battlegroundsTabs;
    object lastBattlegroundsView;
    DateTime lastFlightSample = DateTime.MinValue;
    string lastFlightState;
    bool pending;
    int moves;
    DateTime lastRegionWrite = DateTime.MinValue;
    DateTime lastHoverWrite = DateTime.MinValue;
    DateTime lastWindowWrite = DateTime.MinValue;
    int failureSequence;
    string hoverOutput;
    int tooltipShows, tooltipHides;
    bool tooltipRequested;
    DateTime lastTooltipEvent = DateTime.MinValue;
    int[] lastPreviewBounds;
    bool alphaFloorApplied;
    PortableOverlayInput portableInput;
    NativeClientExport nativeClientExport;
    bool mouseDiagnosticsAttached;
    int overlayMouseDowns, overlayMouseUps;
    string lastMouseTargetType;
    bool mainDiagnosticsAttached;
    int mainMouseDowns, mainMouseUps;
    static bool particleHandlerRegistered;
    static bool suppressArenaParticles;
    static int arenaParticleEmittersSuppressed;
    public void OnLoad()
    {
        nativeClientExport = NativeClientExport.Start();
        var inputFile = Environment.GetEnvironmentVariable("HDT_INPUT_REGION_FILE");
        if (!String.IsNullOrEmpty(inputFile) && Environment.GetEnvironmentVariable("HDT_FLIGHT_RECORDER") != "0")
        {
            var dispatcher = Application.Current.Dispatcher;
            recorder = new FlightRecorder(Path.GetDirectoryName(inputFile), Version.ToString(),
                action => dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, action),
                message => Log.Warn(message));
            CompositionTarget.Rendering += ObserveRendering;
        }
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        suppressArenaParticles = true;
        if (!particleHandlerRegistered)
        {
            // Run before the emitter's instance Loaded handler. That handler
            // builds RenderTargetBitmap-backed 3D particle systems, which
            // reproduce UCEERR_RENDERTHREADFAILURE under the tested Proton.
            // Keep the score and badge artwork; skip only cosmetic particles.
            EventManager.RegisterClassHandler(
                typeof(Hearthstone_Deck_Tracker.Controls.Overlay.Arena.ParticleEmitter),
                FrameworkElement.LoadedEvent, new RoutedEventHandler(SuppressArenaParticleEmitter));
            particleHandlerRegistered = true;
        }
        // The filter runs before HDT's handler opens its modal crash dialog.
        // Observe only: never change RequestCatch or exception handling.
        Application.Current.Dispatcher.UnhandledExceptionFilter += ObserveDispatcherFailure;
        Log.Info("Local Proton compatibility enabled; WPF software rendering requested");
        hoverOutput = Environment.GetEnvironmentVariable("HDT_HOVER_DIAGNOSTICS_FILE");
        if (!String.IsNullOrEmpty(hoverOutput)) OverlayExtensions.OnToolTipChanged += ObserveTooltip;
    }
    public void OnUnload()
    {
        if (nativeClientExport != null) { nativeClientExport.Dispose(); nativeClientExport = null; }
        if (portableInput != null) { portableInput.Stop(); portableInput = null; }
        CompositionTarget.Rendering -= ObserveRendering;
        if (battlegroundsTabs != null) battlegroundsTabs.PropertyChanged -= ObserveBattlegroundsTab;
        battlegroundsTabs = null;
        if (recorder != null) { recorder.Stop(); recorder = null; }
        lastBattlegroundsView = null;
        suppressArenaParticles = false;
        Application.Current.Dispatcher.UnhandledExceptionFilter -= ObserveDispatcherFailure;
        if (!String.IsNullOrEmpty(hoverOutput)) OverlayExtensions.OnToolTipChanged -= ObserveTooltip;
        var overlay = Api.OverlayWindow;
        var main = (Window)(object)Api.MainWindow;
        if (main != null && mainDiagnosticsAttached)
        {
            main.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveMainMouseDown));
            main.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ObserveMainMouseUp));
            mainDiagnosticsAttached = false;
        }
        if (overlay != null && mouseDiagnosticsAttached)
        {
            overlay.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveMouseDown));
            overlay.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ObserveMouseUp));
            mouseDiagnosticsAttached = false;
        }
        if (overlay != null) overlay.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveBattlegroundsPointer));
        Log.Info("Local Proton compatibility stopped; layout refreshes=" + moves);
    }
    static void SuppressArenaParticleEmitter(object sender, RoutedEventArgs e)
    {
        if (!suppressArenaParticles) return;
        // Loaded is a direct routed event. Marking it handled prevents the
        // normal instance handler from initializing the 3D systems or timer.
        e.Handled = true;
        ((FrameworkElement)sender).SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        arenaParticleEmittersSuppressed++;
        if (arenaParticleEmittersSuppressed == 1)
            Log.Info("Proton compatibility suppressed cosmetic Arena score particles");
    }
    void ObserveTooltip(Func<FrameworkElement> tooltip, DependencyObject owner)
    {
        // Observe the request; never invoke the factory or alter tooltip state.
        tooltipRequested = tooltip != null;
        if (tooltipRequested) tooltipShows++; else tooltipHides++;
        lastTooltipEvent = DateTime.UtcNow;
    }
    public void OnButtonPress() { }
    void ObserveDispatcherFailure(object sender, System.Windows.Threading.DispatcherUnhandledExceptionFilterEventArgs e)
    {
        if (recorder != null) recorder.Event("ui.exception", new { type = e.Exception.GetType().FullName, hresult = e.Exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) });
        var regionFile = Environment.GetEnvironmentVariable("HDT_INPUT_REGION_FILE");
        if (String.IsNullOrEmpty(regionFile) || failureSequence >= 32) return;
        var sequence = ++failureSequence;
        try
        {
            var exceptions = new List<object>();
            for (var error = e.Exception; error != null && exceptions.Count < 8; error = error.InnerException)
            {
                var methods = new List<string>();
                var frames = new System.Diagnostics.StackTrace(error, false).GetFrames();
                if (frames != null) foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    if (method != null) methods.Add((method.DeclaringType == null ? "" : method.DeclaringType.FullName + ".") + method.Name);
                }
                exceptions.Add(new { type = error.GetType().FullName, hresult = error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), methods });
            }
            var windows = new List<object>();
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.Dispatcher.CheckAccess())
                {
                    windows.Add(new { type = window.GetType().FullName, uiThread = window.Dispatcher.Thread.ManagedThreadId, inspectedOnOwner = false });
                    continue;
                }
                windows.Add(new
                {
                    type = window.GetType().FullName,
                    width = window.ActualWidth,
                    height = window.ActualHeight,
                    visible = window.IsVisible,
                    active = window.IsActive,
                    allowsTransparency = window.AllowsTransparency
                });
            }
            var output = Path.Combine(Path.GetDirectoryName(regionFile), "dispatcher-failures-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".jsonl");
            File.AppendAllText(output, new JavaScriptSerializer().Serialize(new
            {
                observedUTC = DateTime.UtcNow.ToString("o"),
                sequence,
                captureLimit = 32,
                exceptions,
                windows,
                processRenderMode = RenderOptions.ProcessRenderMode.ToString(),
                sources = DescribeSources()
            }) + Environment.NewLine);
        }
        catch
        {
            // A diagnostic write failure must not replace HDT's original exception.
            Log.Warn("Could not save local dispatcher failure diagnostics");
        }
    }
    static List<object> DescribeSources()
    {
        var sources = new List<object>();
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (!source.Dispatcher.CheckAccess())
            {
                sources.Add(new { sourceType = source.GetType().FullName, uiThread = source.Dispatcher.Thread.ManagedThreadId, inspectedOnOwner = false });
                continue;
            }
            var root = source.RootVisual;
            if (root == null) continue;
            if (!root.Dispatcher.CheckAccess())
            {
                sources.Add(new { rootType = root.GetType().FullName, uiThread = root.Dispatcher.Thread.ManagedThreadId, inspectedOnOwner = false });
                continue;
            }
            var target = source.CompositionTarget as HwndTarget;
            var element = root as FrameworkElement;
            sources.Add(new
            {
                rootType = root.GetType().FullName,
                uiThread = root.Dispatcher.Thread.ManagedThreadId,
                inspectedOnOwner = true,
                renderMode = target == null ? null : target.RenderMode.ToString(),
                width = element == null ? (double?)null : element.ActualWidth,
                height = element == null ? (double?)null : element.ActualHeight,
                visible = element == null ? (bool?)null : element.IsVisible
            });
        }
        return sources;
    }
    public void OnUpdate()
    {
        var overlay = Api.OverlayWindow;
        if (overlay == null || pending) return;
        if (overlay.Dispatcher.CheckAccess()) UpdateUi();
        else
        {
            pending = true;
            overlay.Dispatcher.BeginInvoke((Action)(() => { try { UpdateUi(); } finally { pending = false; } }));
        }
    }
    void UpdateUi()
    {
        if (nativeClientExport != null) nativeClientExport.Capture();
        var overlay = Api.OverlayWindow;
        if (overlay != null && Environment.GetEnvironmentVariable("HDT_DESKTOP_BACKEND") == "x11")
        {
            if (portableInput == null) portableInput = new PortableOverlayInput();
            portableInput.Update(overlay);
        }
        if (overlay != null && recorder != null)
        {
            var tabs = overlay.BattlegroundsGuidesTabsViewModel as INotifyPropertyChanged;
            if (!Object.ReferenceEquals(tabs, battlegroundsTabs))
            {
                if (battlegroundsTabs != null) battlegroundsTabs.PropertyChanged -= ObserveBattlegroundsTab;
                battlegroundsTabs = tabs;
                lastBattlegroundsView = tabs == null ? null : overlay.BattlegroundsGuidesTabsViewModel.ActiveViewModel;
                if (tabs != null) tabs.PropertyChanged += ObserveBattlegroundsTab;
                overlay.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveBattlegroundsPointer));
                overlay.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveBattlegroundsPointer), true);
            }
            if ((DateTime.UtcNow - lastFlightSample).TotalSeconds >= 1)
            {
                var arena = overlay.ArenaPickHelperViewModel;
                string state = Api.Game.CurrentMode + "/" + Api.Game.IsInMenu + "/" + arena.ClientState + "/" + arena.SessionState + "/" + arena.IsOverlayVisible;
                if (state != lastFlightState)
                {
                    recorder.Event("view.state", new
                    {
                        mode = Api.Game.CurrentMode.ToString(),
                        inMenu = Api.Game.IsInMenu,
                        arenaClientState = arena.ClientState.ToString(),
                        arenaSessionState = arena.SessionState.ToString(),
                        arenaVisible = arena.IsOverlayVisible
                    });
                    lastFlightState = state;
                }
                lastFlightSample = DateTime.UtcNow;
            }
        }
        if (overlay != null && !mouseDiagnosticsAttached && !String.IsNullOrEmpty(hoverOutput))
        {
            overlay.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveMouseDown), true);
            overlay.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ObserveMouseUp), true);
            mouseDiagnosticsAttached = true;
        }
        if (overlay != null && !alphaFloorApplied && Environment.GetEnvironmentVariable("HDT_ALPHA_FLOOR") == "1")
        {
            // Experiment: avoid exactly zero-alpha background in Wine's layered-window path.
            // Local to this window; a restart without the flag restores HDT's own background.
            overlay.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            alphaFloorApplied = true;
            Log.Info("Local Proton overlay alpha-floor experiment enabled (1/255 black)");
        }
        if (overlay != null && overlay.IsContentVisible)
        {
            var rect = Hearthstone_Deck_Tracker.User32.GetHearthstoneRect(true);
            if (rect.Width > 0 && rect.Height > 0 &&
                (overlay.Left != rect.Left || overlay.Top != rect.Top || overlay.Width != rect.Width || overlay.Height != rect.Height))
            {
                overlay.UpdatePosition();
                moves++;
                if (moves == 1 || moves % 20 == 0) Log.Info("Local Proton overlay layout refresh " + moves);
            }
        }
        var main = (Window)(object)Api.MainWindow;
        if (main != null && !mainDiagnosticsAttached)
        {
            main.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ObserveMainMouseDown), true);
            main.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ObserveMainMouseUp), true);
            mainDiagnosticsAttached = true;
        }
        // Ask WPF to refresh its own mouse-over state; no synthetic native clicks/movement.
        // Covers templated/native tooltip hosts without a second tree/tooltip state model.
        if (main != null && main.IsVisible && main.IsActive) Mouse.Synchronize();
        if (!String.IsNullOrEmpty(hoverOutput) && (DateTime.UtcNow - lastHoverWrite).TotalMilliseconds >= 500)
        {
            int openTooltips = 0, visibleCardPreviews = 0;
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                var root = source.RootVisual;
                if (root != null) CountPreviews(root, ref openTooltips, ref visibleCardPreviews);
            }
            var json = new JavaScriptSerializer().Serialize(new
            {
                observedUTC = DateTime.UtcNow.ToString("o"),
                tooltipShows,
                tooltipHides,
                tooltipRequested,
                lastTooltipEventUTC = lastTooltipEvent.ToString("o"),
                openTooltips,
                visibleCardPreviews,
                overlayVisible = overlay != null && overlay.IsVisible,
                lastPreviewBounds,
                alphaFloorApplied,
                overlayMouseDowns,
                overlayMouseUps,
                lastMouseTargetType
            });
            File.WriteAllText(hoverOutput + ".next", json);
            if (File.Exists(hoverOutput)) File.Replace(hoverOutput + ".next", hoverOutput, null);
            else File.Move(hoverOutput + ".next", hoverOutput);
            lastHoverWrite = DateTime.UtcNow;
        }
        var output = Environment.GetEnvironmentVariable("HDT_INPUT_REGION_FILE");
        if (!String.IsNullOrEmpty(output) && (DateTime.UtcNow - lastWindowWrite).TotalMilliseconds >= 1000)
        {
            var snapshot = Path.Combine(Path.GetDirectoryName(output), "presentation-state.json");
            var json = new JavaScriptSerializer().Serialize(new
            {
                observedUTC = DateTime.UtcNow.ToString("o"),
                processRenderMode = RenderOptions.ProcessRenderMode.ToString(),
                sources = DescribeSources()
            });
            File.WriteAllText(snapshot + ".next", json);
            if (File.Exists(snapshot)) File.Replace(snapshot + ".next", snapshot, null);
            else File.Move(snapshot + ".next", snapshot);
            lastWindowWrite = DateTime.UtcNow;
        }
        if (overlay != null && !String.IsNullOrEmpty(output) && (DateTime.UtcNow - lastRegionWrite).TotalMilliseconds >= 200)
        {
            var source = PresentationSource.FromVisual(overlay);
            if (source != null && source.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformToDevice;
                var clip = Rect.Transform(new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight), matrix);
                var regions = new List<int[]>();
                if (overlay.IsContentVisible) CollectControls(overlay, overlay, matrix, clip, regions);
                var arena = overlay.ArenaPickHelperViewModel;
                var arenaVisual = overlay.FindName("ArenaPickHelper") as FrameworkElement;
                var json = new JavaScriptSerializer().Serialize(new
                {
                    session = Environment.GetEnvironmentVariable("HDT_INPUT_SESSION"),
                    overlayHwnd = new WindowInteropHelper(overlay).Handle.ToInt64(),
                    observedMS = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    width = (int)Math.Round(clip.Width),
                    height = (int)Math.Round(clip.Height),
                    rectangles = regions,
                    state = new
                    {
                        mode = Api.Game.CurrentMode.ToString(),
                        inMenu = Api.Game.IsInMenu,
                        setupDone = Api.Game.SetupDone,
                        hasSelectedDeck = Api.Game.CurrentSelectedDeck != null,
                        arenaClientState = arena.ClientState.ToString(),
                        arenaSessionState = arena.SessionState.ToString(),
                        arenaDrafting = arena.IsDrafting,
                        arenaAvailable = arena.IsArenasmithAvailable,
                        arenaRequestedVisible = arena.IsOverlayVisible,
                        arenaVisualVisibility = arenaVisual == null ? null : arenaVisual.Visibility.ToString(),
                        arenaVisualOpacity = arenaVisual == null ? (double?)null : arenaVisual.Opacity,
                        mainActive = main != null && main.IsActive,
                        mainEnabled = main != null && main.IsEnabled,
                        mainMouseDowns = mainMouseDowns,
                        mainMouseUps = mainMouseUps,
                        arenaParticleEmittersSuppressed = arenaParticleEmittersSuppressed,
                        flightRecorderFailure = recorder == null ? null : recorder.FailureType
                    }
                });
                File.WriteAllText(output + ".next", json);
                // Same-directory replacement keeps readers from observing a half-written snapshot.
                if (File.Exists(output)) File.Replace(output + ".next", output, null);
                else File.Move(output + ".next", output);
                lastRegionWrite = DateTime.UtcNow;
            }
        }
    }
    void ObserveMouseDown(object sender, MouseButtonEventArgs e)
    {
        overlayMouseDowns++; lastMouseTargetType = e.OriginalSource == null ? null : e.OriginalSource.GetType().Name;
    }
    void ObserveMainMouseDown(object sender, MouseButtonEventArgs e)
    {
        mainMouseDowns++;
        if (recorder != null) recorder.Event("main.pointer.down", new { targetType = e.OriginalSource == null ? null : e.OriginalSource.GetType().Name });
    }
    void ObserveRendering(object sender, EventArgs e) { if (recorder != null) recorder.Rendered(); }
    void ObserveBattlegroundsTab(object sender, PropertyChangedEventArgs e)
    {
        if (recorder == null || e.PropertyName != "ActiveViewModel") return;
        var tabs = sender as Hearthstone_Deck_Tracker.Controls.Overlay.Battlegrounds.Guides.BattlegroundsGuidesTabsViewModel;
        if (tabs == null) return;
        var active = tabs.ActiveViewModel;
        if (Object.ReferenceEquals(active, lastBattlegroundsView)) return;
        lastBattlegroundsView = active;
        recorder.Event("battlegrounds.tab.changed", new { viewType = active == null ? null : active.GetType().Name });
    }
    void ObserveBattlegroundsPointer(object sender, MouseButtonEventArgs e)
    {
        if (recorder == null || e.ChangedButton != MouseButton.Left) return;
        var node = e.OriginalSource as DependencyObject;
        for (int depth = 0; node != null && depth < 32; depth++)
        {
            var button = node as Hearthstone_Deck_Tracker.Controls.Overlay.Battlegrounds.OverlayButton;
            if (button != null)
            {
                var binding = BindingOperations.GetBindingExpression(button, Hearthstone_Deck_Tracker.Controls.Overlay.Battlegrounds.OverlayButton.CommandProperty);
                if (binding != null && binding.ParentBinding.Path != null && binding.ParentBinding.Path.Path == "ShowMinionsCommand" && Object.ReferenceEquals(button.DataContext, battlegroundsTabs))
                    recorder.Event("battlegrounds.minions.pointer.down", new { });
                return;
            }
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
    }
    void ObserveMainMouseUp(object sender, MouseButtonEventArgs e) { mainMouseUps++; }
    void ObserveMouseUp(object sender, MouseButtonEventArgs e)
    {
        overlayMouseUps++; lastMouseTargetType = e.OriginalSource == null ? null : e.OriginalSource.GetType().Name;
    }
    void CountPreviews(DependencyObject node, ref int openTooltips, ref int visibleCardPreviews)
    {
        var tooltip = node as ToolTip;
        if (tooltip != null && tooltip.IsOpen) openTooltips++;
        var element = node as FrameworkElement;
        if (element != null && element.IsVisible && element.GetType().Name == "CardTooltip")
        {
            visibleCardPreviews++;
            var a = element.PointToScreen(new Point(0, 0));
            var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            lastPreviewBounds = new[] { (int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Ceiling(b.X - a.X), (int)Math.Ceiling(b.Y - a.Y) };
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            CountPreviews(VisualTreeHelper.GetChild(node, i), ref openTooltips, ref visibleCardPreviews);
    }
    static void CollectControls(DependencyObject node, Window overlay, Matrix device, Rect clip, List<int[]> regions)
    {
        var e = node as FrameworkElement;
        if (e != null && (!e.IsVisible || e.Opacity == 0)) return;
        if (e != null && OverlayExtensions.GetIsOverlayHitTestVisible(e) && e.ActualWidth > 0 && e.ActualHeight > 0)
        {
            var bounds = Rect.Transform(e.TransformToAncestor(overlay).TransformBounds(new Rect(e.RenderSize)), device);
            bounds.Intersect(clip);
            if (!bounds.IsEmpty)
            {
                int left = (int)Math.Floor(bounds.Left), top = (int)Math.Floor(bounds.Top);
                int right = (int)Math.Ceiling(bounds.Right), bottom = (int)Math.Ceiling(bounds.Bottom);
                if (right > left && bottom > top) regions.Add(new[] { left, top, right - left, bottom - top });
            }
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CollectControls(VisualTreeHelper.GetChild(node, i), overlay, device, clip, regions);
    }
}
