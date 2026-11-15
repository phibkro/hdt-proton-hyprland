using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// A single writer owns serialization, files, rotation and the watchdog.
// Producers enqueue bounded, account-free values and never wait for disk IO.
public sealed class FlightRecorder {
    const int QueueLimit=512, FileLimit=1024*1024;
    readonly object gate=new object();
    readonly Queue<object> queue=new Queue<object>();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Action<Action> dispatch;
    readonly Action<string> warning;
    readonly string directory,session;
    readonly Thread worker;
    long sequence,dropped,pingStarted,renderCallbacks;
    int pingPending;
    bool stopping,stallReported,captured;
    volatile string failureType;
    public string FailureType {get {return failureType;}}
    public FlightRecorder(string evidence,string version,Action<Action> post,Action<string> warn) {
        dispatch=post; warning=warn;
        session=Guid.NewGuid().ToString("N");
        // A reload can start before the old writer drains. Each owns its files.
        directory=Path.Combine(evidence,"flight-recorder-"+Process.GetCurrentProcess().Id+"-"+session);
        Event("session.start",new {utc=DateTime.UtcNow.ToString("o"),pid=Process.GetCurrentProcess().Id,
            runtime=Environment.Version.ToString(),pluginVersion=version,frequency=Stopwatch.Frequency});
        worker=new Thread(Run) {IsBackground=true,Name="HDT local flight recorder"};
        worker.Start();
    }
    public void Event(string name,object data) {
        lock(gate) {
            if(stopping) return;
            if(queue.Count>=QueueLimit) { Interlocked.Increment(ref dropped); return; }
            queue.Enqueue(new {schema=1,session,seq=++sequence,tsUS=ToMicroseconds(clock.ElapsedTicks,Stopwatch.Frequency),
                thread=Thread.CurrentThread.ManagedThreadId,name,data});
        }
    }
    static long ToMicroseconds(long ticks,long frequency) {
        // Multiplying the full tick count first overflows in long sessions.
        return (ticks/frequency)*1000000L+(ticks%frequency)*1000000L/frequency;
    }
    public void Rendered() {Interlocked.Increment(ref renderCallbacks);}
    public void Stop() {
        Event("session.stop",new {});
        lock(gate) stopping=true;
        // Do not join the writer from WPF's dispatcher. A process termination
        // can lose the final batch, but must never hold the UI waiting for IO.
    }
    void Ping() {
        long now=clock.ElapsedMilliseconds;
        if(stallReported && Volatile.Read(ref pingPending)==0) {
            Event("ui.recovered",new {});
            stallReported=false;
        }
        if(Interlocked.CompareExchange(ref pingPending,1,0)==0) {
            Interlocked.Exchange(ref pingStarted,now);
            dispatch(delegate {
                long duration=clock.ElapsedMilliseconds-Interlocked.Read(ref pingStarted);
                Event("ui.dispatch",new {durationMS=duration});
                Interlocked.Exchange(ref pingPending,0);
            });
        } else if(now-Interlocked.Read(ref pingStarted)>=2000 && !stallReported) {
            stallReported=true;
            Event("ui.stall",new {waitingMS=now-Interlocked.Read(ref pingStarted),thresholdMS=2000});
        }
    }
    void Run() {
        try {
            Directory.CreateDirectory(directory);
            var assembly=typeof(FlightRecorder).Assembly.Location;
            using(var hash=SHA256.Create()) using(var input=File.OpenRead(assembly))
                Event("build.identity",new {pluginSHA256=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant()});
            var json=new JavaScriptSerializer();
            long lastSample=-1000;
            while(true) {
                bool stop;
                lock(gate) stop=stopping;
                if(!stop && clock.ElapsedMilliseconds-lastSample>=1000) {
                    Ping();
                    Event("render.sample",new {callbacks=Interlocked.Read(ref renderCallbacks),dropped=Interlocked.Read(ref dropped)});
                    lastSample=clock.ElapsedMilliseconds;
                }
                var path=Path.Combine(directory,"events.jsonl");
                using(var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read)) {
                    while(true) {
                        if(stream.Length>FileLimit-16384) break;
                        object item;
                        lock(gate) {if(queue.Count==0) break; item=queue.Dequeue();}
                        byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(item)+"\n");
                        if(bytes.Length>16384) {Interlocked.Increment(ref dropped);continue;}
                        stream.Write(bytes,0,bytes.Length);
                    }
                    stream.Flush();
                }
                // Reserve enough room for the largest event produced here.
                if(new FileInfo(path).Length>FileLimit-16384) {
                    string previous=Path.Combine(directory,"previous.jsonl");
                    if(File.Exists(previous)) File.Delete(previous);
                    File.Move(path,previous);
                }
                if(stallReported && !captured) {
                    foreach(string name in new[]{"previous.jsonl","events.jsonl"}) {
                        string source=Path.Combine(directory,name);
                        if(File.Exists(source)) File.Copy(source,Path.Combine(directory,"first-stall-"+name),true);
                    }
                    captured=true;
                }
                lock(gate) {if(stop && queue.Count==0) break;}
                Thread.Sleep(250);
            }
        } catch(Exception error) {
            // Report only the type; IO exception messages can contain user paths.
            failureType=error.GetType().Name;
            lock(gate) stopping=true;
            try {warning("Local flight recorder stopped: "+failureType);} catch {
                // Logging may fail for the same IO reason. The existing input
                // snapshot also exposes FailureType without another file writer.
            }
        }
    }
}
