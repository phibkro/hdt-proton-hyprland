using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// Runs in the same private Wine namespace as the WPF replay, never in HDT.
sealed class FlightRecorderChecks
{
    static int Main(string[] args)
    {
        var convert = typeof(FlightRecorder).GetMethod("ToMicroseconds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if ((long)convert.Invoke(null, new object[] { long.MaxValue, 1000000000L }) != 9223372036854775L) return 5;
        string root = args[0];
        int errors = 0;
        var recorder = new FlightRecorder(root, "fixture", action => action(), message => Interlocked.Increment(ref errors));
        var data = new { padding = new string('x', 9000) };
        // Overload the bounded producer queue, then allow the writer to rotate.
        for (int i = 0; i < 20000; i++) recorder.Event("fixture.pressure", data);
        Thread.Sleep(2200);
        recorder.Stop();
        Thread.Sleep(1000);
        var directory = Directory.GetDirectories(root, "flight-recorder-*").Single();
        var files = Directory.GetFiles(directory, "*.jsonl");
        if (errors != 0 || files.Length != 2 || files.Any(path => new FileInfo(path).Length > 1024 * 1024)) return 1;
        var json = new JavaScriptSerializer();
        var records = files.SelectMany(File.ReadAllLines).Select(line => json.Deserialize<Dictionary<string, object>>(line)).ToArray();
        if (!records.Any(e => (string)e["name"] == "session.stop")) return 2;
        if (!records.Any(e => (string)e["name"] == "render.sample" && Convert.ToInt64(((Dictionary<string, object>)e["data"])["dropped"], System.Globalization.CultureInfo.InvariantCulture) > 0)) return 3;
        // A failed writer must report failure without throwing on a producer.
        string blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "");
        var failed = new FlightRecorder(blocked, "fixture", action => action(), message =>
        {
            Interlocked.Increment(ref errors);
            throw new IOException("fixture logging also failed");
        });
        Thread.Sleep(500); failed.Event("fixture.after-failure", new { }); failed.Stop();
        if (errors != 1 || failed.FailureType == null) return 4;
        Console.WriteLine("Verified rotation, queue-drop reporting, shutdown drain, and IO failure isolation.");
        return 0;
    }
}
