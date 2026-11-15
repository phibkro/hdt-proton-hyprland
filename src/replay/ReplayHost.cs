// Isolated UI fixture host. Uses the installed HDT windows; no game client.
using System;
using System.IO;
using System.Reflection;
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

class ReplayHost {
    static string output;
    static string directory;
    static Window main,overlay;
    static Assembly hdt;
    static object arena;
    static int commandId,mouseDowns,exitCode;
    static bool finished;
    static string scene="menu";
    static Popup popup;
    static JavaScriptSerializer serializer=new JavaScriptSerializer();
    static void Record(string phase, Exception error) {
        for(var current=error;current!=null;current=current.InnerException)
            File.AppendAllText(output,phase+" "+current.GetType().FullName+" "+current.HResult.ToString("X8")+" "+current.Message+Environment.NewLine+current.StackTrace+Environment.NewLine);
    }
    [STAThread]
    static int Main(string[] args) {
        var setup=new AppDomainSetup {
            ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,
            ConfigurationFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HearthstoneDeckTracker.exe.config")
        };
        var domain=AppDomain.CreateDomain("HDT isolated replay",null,setup);
        var runner=(ReplayRunner)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location,typeof(ReplayRunner).FullName);
        return runner.Run(args);
    }
    public static int Run(string[] args) {
        if(args.Length!=1) return 2;
        output=args[0];
        try {
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            directory=Path.GetDirectoryName(output);
            hdt=Assembly.LoadFrom("HearthstoneDeckTracker.exe");
            Application.ResourceAssembly=hdt;
            var app=(Application)Activator.CreateInstance(hdt.GetType("Hearthstone_Deck_Tracker.App",true));
            app.GetType().GetMethod("InitializeComponent").Invoke(app,null);
            var startup=app.GetType().GetMethod("App_OnStartup",BindingFlags.Instance|BindingFlags.NonPublic);
            app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.UnhandledExceptionFilter += (sender,e) => Record("dispatcher",e.Exception);
            var core=hdt.GetType("Hearthstone_Deck_Tracker.Core",true);
            main=(Window)core.GetProperty("MainWindow").GetValue(null,null);
            main.Left=20;main.Top=20;main.Width=1100;main.Height=650;
            main.Show();
            overlay=(Window)core.GetProperty("Overlay").GetValue(null,null);
            overlay.Left=1250;overlay.Top=120;overlay.Width=1000;overlay.Height=750;
            overlay.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));
            overlay.GetType().GetMethod("ShowOverlay").Invoke(overlay,new object[]{true});
            arena=Get(overlay,"ArenaPickHelperViewModel");
            main.AddHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent,new System.Windows.Input.MouseButtonEventHandler((sender,e)=>mouseDowns++),true);
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            bool firstTick=true;
            timer.Tick+=(sender,e)=> {
                try {
                    if(firstTick) File.AppendAllText(output,"first-tick"+Environment.NewLine);
                    ProcessCommand();
                    if(finished) return;
                    Snapshot();
                    if(firstTick) File.AppendAllText(output,"first-snapshot"+Environment.NewLine);
                    firstTick=false;
                }
                catch(Exception error) { Record("command",error);exitCode=1;Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            };
            timer.Start();
            File.AppendAllText(output,"ready main="+main.GetType().FullName+" overlay="+overlay.GetType().FullName+Environment.NewLine);
            // Use the normal WPF pump, with HDT's Core.Initialize startup handler
            // removed. Constructor side effects still run inside network isolation.
            app.Run();
            return exitCode;
        } catch(Exception error) {
            while(error is TargetInvocationException && error.InnerException!=null) error=error.InnerException;
            Record("startup",error);return 1;
        }
    }
    static object Get(object target,string name) {
        var p=target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(p!=null) return p.GetValue(target,null);
        var f=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(f==null) throw new MissingMemberException(target.GetType().FullName,name);
        return f.GetValue(target);
    }
    static void Set(object target,string name,object value) {
        var p=target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(p!=null) {p.SetValue(target,value,null);return;}
        var f=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(f==null) throw new MissingMemberException(target.GetType().FullName,name);
        f.SetValue(target,value);
    }
    static void SetEnum(object target,string name,string value) {
        var property=target.GetType().GetProperty(name);
        if(property==null) throw new MissingMemberException(target.GetType().FullName,name);
        property.SetValue(target,Enum.Parse(property.PropertyType,value),null);
    }
    static object EnsureChild(object target,string name) {
        var value=Get(target,name);
        if(value!=null) return value;
        var p=target.GetType().GetProperty(name);
        var f=target.GetType().GetField(name);
        var type=p!=null?p.PropertyType:f.FieldType;
        value=Activator.CreateInstance(type);Set(target,name,value);return value;
    }
    static object MakeCard(Type cardType,string id,int index) {
        var ctor=cardType.GetConstructors().Single(c=>c.GetParameters().Length==5);
        var data=Activator.CreateInstance(ctor.GetParameters()[1].ParameterType);
        var scores=EnsureChild(data,"Arenasmith");
        Set(scores,"Score",(scene=="draft-1"?60+index:80+index).ToString());
        Set(scores,"Plaque",index*2+1);
        EnsureChild(data,"Messages");
        var related=EnsureChild(data,"RelatedCards");
        var generated=EnsureChild(related,"GeneratedCardIds");
        EnsureChild(generated,"Generated");
        return ctor.Invoke(new object[]{id,data,new List<string>(),true,false});
    }
    static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T) yield return (T)child;
            foreach(var nested in Children<T>(child)) yield return nested;
        }
    }
    static void SetScene(string value) {
        scene=value;
        var game=hdt.GetType("Hearthstone_Deck_Tracker.Core").GetProperty("Game").GetValue(null,null);
        Set(game,"IsRunning",true);Set(game,"IsInMenu",true);
        SetEnum(game,"CurrentMode",value=="menu"?"HUB":"DRAFT");
        var sceneProperty=hdt.GetType("Hearthstone_Deck_Tracker.SceneHandler").GetProperty("Scene");
        sceneProperty.SetValue(null,Get(game,"CurrentMode"),null);
        bool drafting=value.StartsWith("draft-");
        SetEnum(arena,"ClientState",drafting?"Underground_Draft":value=="menu"?"None":"Underground_Landing");
        SetEnum(arena,"SessionState",drafting?"DRAFTING":"INVALID");
        Set(arena,"IsDrafting",drafting);Set(arena,"IsAnimating",false);
        Set(arena,"IsArenasmithAvailable",drafting);Set(arena,"IsOverlayVisible",drafting);
        if(drafting) {
            var cardType=hdt.GetType("Hearthstone_Deck_Tracker.Controls.Overlay.Arena.ArenaPickSingleCardOptionViewModel",true);
            var property=arena.GetType().GetProperty("CardStats");
            var cards=(IList)Activator.CreateInstance(property.PropertyType);
            var ids=value=="draft-1"?new[]{"CS2_029","CS2_032","CS2_172"}:new[]{"CS2_024","CS2_025","CS2_182"};
            for(int i=0;i<ids.Length;i++) cards.Add(MakeCard(cardType,ids[i],i));
            property.SetValue(arena,cards,null);
            SetEnum(arena,"StateChange","FadeIn");
        }
        overlay.GetType().GetMethod("UpdateArenaPickHelperVisibility",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(overlay,null);
    }
    static void ProcessCommand() {
        var path=Path.Combine(directory,"command.json");
        if(!File.Exists(path)) return;
        var command=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
        int id=Convert.ToInt32(command["id"]);
        if(id<=commandId) return;
        string op=(string)command["op"];
        switch(op) {
            case "menu":case "arena-lobby":case "draft-1":case "draft-2":SetScene(op);break;
            case "settings-close":Set(main.FindName("FlyoutOptions"),"IsOpen",false);break;
            case "popup-show":
                popup=new Popup {PlacementTarget=main,Placement=PlacementMode.Center,AllowsTransparency=true,
                    Child=new Border {Background=Brushes.DarkBlue,Padding=new Thickness(20),Child=new TextBlock {Text="Replay popup fixture",Foreground=Brushes.White}}};
                popup.IsOpen=true;break;
            case "popup-hide":if(popup!=null)popup.IsOpen=false;break;
            case "new":
                var menu=main.FindName("MainWindowMenu") as FrameworkElement;
                var newDeckCommand=Get(menu.DataContext,"NewDeckCommand") as System.Windows.Input.ICommand;
                if(newDeckCommand==null || !newDeckCommand.CanExecute("Mage")) throw new InvalidOperationException("New deck command unavailable");
                newDeckCommand.Execute("Mage");break;
            case "finish":
                commandId=id;Snapshot();finished=true;
                hdt.GetType("Hearthstone_Deck_Tracker.Core").GetProperty("IsShuttingDown").SetValue(null,true,null);
                Application.Current.Shutdown(exitCode);return;
            default:throw new InvalidOperationException("Unknown replay command");
        }
        commandId=id;
    }
    static void Snapshot() {
        main.UpdateLayout();
        var button=Children<Button>(main).FirstOrDefault(b=>b.IsVisible && Children<TextBlock>(b).Any(t=>t.Text.Equals("settings",StringComparison.OrdinalIgnoreCase)));
        object bounds=null;
        if(button!=null) {
            var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
            bounds=new {x=(int)point.X,y=(int)point.Y};
        }
        var view=overlay.FindName("ArenaPickHelper") as FrameworkElement;
        var cards=Get(arena,"CardStats") as IEnumerable;
        var ids=new List<string>();
        if(cards!=null) foreach(var card in cards) ids.Add((string)Get(Get(card,"Card"),"Id"));
        var data=new {observedUTC=DateTime.UtcNow.ToString("o"),commandId,scene,mouseDowns,
            mainWin32Handle=new WindowInteropHelper(main).Handle.ToInt64(),mainWidth=main.ActualWidth,mainHeight=main.ActualHeight,
            settingsButton=bounds,settingsOpen=(bool)Get(main.FindName("FlyoutOptions"),"IsOpen"),
            arenaVisibility=view.Visibility.ToString(),arenaOpacity=view.Opacity,cards=ids,
            popupOpen=popup!=null && popup.IsOpen,
            visibleDialogTypes=Children<FrameworkElement>(main).Where(e=>e.IsVisible && e.GetType().Name.EndsWith("Dialog")).Select(e=>e.GetType().FullName).Distinct().ToArray(),
            visibleScores=Children<FrameworkElement>(overlay).Where(t=>t.IsVisible && t.GetType().Name=="HearthstoneTextBlock")
                .Select(t=>(string)Get(t,"Text")).Where(t=>new[]{"60","61","62","80","81","82"}.Contains(t)).Distinct().OrderBy(t=>t).ToArray()};
        var path=Path.Combine(directory,"state.json");
        File.WriteAllText(path+".next",serializer.Serialize(data));
        if(File.Exists(path)) File.Replace(path+".next",path,null);else File.Move(path+".next",path);
    }
}

public class ReplayRunner : MarshalByRefObject {
    public int Run(string[] args) { return ReplayHost.Run(args); }
}
