#if NET10_0_OR_GREATER
// C# 5 represents an empty pending slot and temporary path with null.
#pragma warning disable 8600, 8618, 8625
#endif
// Original bounded latest-only publisher, shared with native acceptance tests.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class NativeSnapshotWriter : IDisposable
{
    readonly string output;
    readonly Func<object, byte[]> serialize;
    readonly Action failure;
    readonly object gate = new object();
    readonly AutoResetEvent signal = new AutoResetEvent(false);
    readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    object pending;
    bool stopped;
    public Task Completion { get { return completion.Task; } }
    public NativeSnapshotWriter(string path, Func<object, byte[]> serializer, Action onFailure)
    {
        output = Path.GetFullPath(path);
        serialize = serializer;
        failure = onFailure;
        var worker = new Thread(WriteLoop) { IsBackground = true, Name = "HDT native observation writer" };
        try { worker.Start(); }
        catch { signal.Dispose(); throw; }
    }
    public void Offer(object snapshot)
    {
        lock (gate) { if (stopped) return; pending = snapshot; signal.Set(); }
    }
    public void Dispose()
    {
        lock (gate) { if (stopped) return; stopped = true; pending = null; signal.Set(); }
        // The worker owns disposal; a blocked external write must never block UI unload.
        GC.SuppressFinalize(this);
    }
    void WriteLoop()
    {
        try
        {
            while (true)
            {
                signal.WaitOne();
                object snapshot;
                lock (gate) { if (stopped) return; snapshot = pending; pending = null; }
                if (snapshot == null) continue;
                string temporary = null;
                try
                {
                    var bytes = serialize(snapshot);
                    if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException();
                    // Caller supplies an existing private directory. Never create a public one.
                    temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                    if (File.Exists(output)) File.Replace(temporary, output, null); else File.Move(temporary, output);
                    temporary = null;
                }
                catch (Exception) { try { failure(); } catch (Exception) { /* Logging failure must not terminate the worker/application. */ } }
                finally { if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
            }
        }
        finally { signal.Dispose(); completion.TrySetResult(true); }
    }
}
