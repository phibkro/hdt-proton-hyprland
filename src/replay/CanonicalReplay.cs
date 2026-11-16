// Runs the installed HDT parser and publishers, without starting its log-file
// watcher (that watcher can move/delete logs). Only used in the isolated host.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;

// The isolated process owns the writer and HDT's static publisher subscriptions together.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The replay process exits as a unit. Disposing the writer while HDT static publishers remain subscribed would lose canonical events.")]
public sealed class CanonicalReplay
{
    readonly Assembly hdt, watcher;
    readonly object manager;
    readonly MethodInfo handle;
    readonly Type lineType;
    readonly StreamWriter writer;
    readonly object gate = new object();
    readonly System.Threading.AsyncLocal<long> origin = new System.Threading.AsyncLocal<long>();
    long sequence, input;
    public CanonicalReplay(Assembly assembly, string directory)
    {
        hdt = assembly;
        watcher = Assembly.LoadFrom("HearthWatcher.dll");
        lineType = watcher.GetType("HearthWatcher.LogReader.LogLine", true);
        writer = new StreamWriter(new FileStream(Path.Combine(directory, "canonical.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        writer.AutoFlush = true;
        Emit("identity", new { hdtSHA256 = Hash(hdt.Location), watcherSHA256 = Hash(watcher.Location), mvid = hdt.ManifestModule.ModuleVersionId.ToString(), scope = "installed-parser-with-isolated-WPF-host" });
        var log = hdt.GetType("Hearthstone_Deck_Tracker.Utility.Logging.Log", true);
        log.GetEvent("OnLogLine").AddEventHandler(null, new Action<string>(line => Emit("hdt.log", new { line = line })));
        log.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        foreach (var field in hdt.GetType("Hearthstone_Deck_Tracker.API.GameEvents", true).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            var list = field.GetValue(null);
            var add = list.GetType().GetMethod("Add");
            var signature = add.GetParameters()[0].ParameterType;
            object receiver;
            MethodInfo method;
            if (signature == typeof(Action))
            {
                receiver = new EmptySink(this, field.Name); method = receiver.GetType().GetMethod("Receive");
            }
            else
            {
                var sink = typeof(ValueSink<>).MakeGenericType(signature.GetGenericArguments());
                receiver = Activator.CreateInstance(sink, this, field.Name); method = sink.GetMethod("Receive");
            }
            add.Invoke(list, new object[] { Delegate.CreateDelegate(signature, receiver, method) });
            Emit("subscription", new { name = field.Name, delegateType = signature.ToString() });
        }
        var core = hdt.GetType("Hearthstone_Deck_Tracker.Core", true);
        var game = core.GetProperty("Game").GetValue(null, null);
        var managerType = hdt.GetType("Hearthstone_Deck_Tracker.LogReader.LogWatcherManager", true);
        manager = Activator.CreateInstance(managerType);
        managerType.GetMethod("InitializeGameState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, new[] { game });
        handle = managerType.GetMethod("OnNewLines", BindingFlags.Instance | BindingFlags.NonPublic);
        Emit("ready", new { });
    }
    static string Hash(string path)
    {
        using (var algorithm = SHA256.Create()) using (var file = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
    public void Emit(string kind, object data)
    {
        lock (gate)
        {
            writer.WriteLine(JsonConvert.SerializeObject(new { seq = ++sequence, input = origin.Value, utc = DateTime.UtcNow.ToString("o"), kind = kind, data = data }, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Error, Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() } }));
        }
    }
    public void Feed(string ns, string line)
    {
        input++;
        var previous = origin.Value;
        origin.Value = input;
        Emit("parser.enter", new { ns = ns, line = line });
        try
        {
            var lines = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(lineType));
            lines.Add(Activator.CreateInstance(lineType, new object[] { ns, line }));
            handle.Invoke(manager, new object[] { lines });
            Emit("parser.return", new { });
        }
        catch (Exception error)
        {
            var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
            Emit("parser.error", new { type = cause.GetType().FullName, hresult = cause.HResult }); throw;
        }
        finally { origin.Value = previous; }
    }
    public sealed class EmptySink
    {
        readonly CanonicalReplay owner; readonly string name;
        public EmptySink(CanonicalReplay owner, string name) { this.owner = owner; this.name = name; }
        public void Receive() { owner.Emit("hdt.event", new { name = name }); }
    }
    public sealed class ValueSink<T>
    {
        readonly CanonicalReplay owner; readonly string name;
        public ValueSink(CanonicalReplay owner, string name) { this.owner = owner; this.name = name; }
        public void Receive(T value)
        {
            // Never label a partial payload as a faithfully captured event.
            try { owner.Emit("hdt.event", new { name = name, argumentType = typeof(T).AssemblyQualifiedName, argumentRepresentation = "newtonsoft-public-json", value = value }); }
            catch (Exception error) { owner.Emit("hdt.event.capture-failed", new { name = name, type = error.GetType().FullName }); }
        }
    }
}
