using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hdt.Avalonia;

// One owned child at a time. Rust pins the opened inode, rejects nonregular/symlink
// inputs, and bounds bytes. The managed actor separately bounds pipes and lifetime.
public static class SnapshotFiles
{
    public const int ReplayBytes = 8 * 1024 * 1024;
    public const int StatusBytes = 64 * 1024;
    public const int SettingsBytes = 16 * 1024;
    private static readonly SemaphoreSlim Slot = new(1, 1);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public static Task<T> ReadJsonAsync<T>(string path, int maximum, CancellationToken cancellation = default)
        => InvokeJsonAsync<T>(["read-snapshot", maximum.ToString(System.Globalization.CultureInfo.InvariantCulture), Path.GetFullPath(path)], maximum, cancellation);

    public static Task<T> InvokeJsonAsync<T>(IReadOnlyList<string> arguments, int maximum, CancellationToken cancellation = default, string? standardInput = null)
        => Task.Run(async () =>
        {
            var bytes = await ExecuteAsync(arguments, maximum, cancellation, standardInput).ConfigureAwait(false);
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, ReplaySource.JsonOptions)
                    ?? throw new InvalidDataException("Native response must not be null.");
            }
            catch (NotSupportedException error)
            {
                // Missing polymorphic tags are unsupported input, not an unhandled UI failure.
                throw new InvalidDataException("Native response has an unsupported or missing observation shape.", error);
            }
        }, cancellation);

    internal static string HostBinary()
    {
        var host = Environment.GetEnvironmentVariable("HDT_HOST_BINARY");
        if (string.IsNullOrWhiteSpace(host) || !Path.IsPathFullyQualified(host))
            throw new IOException("Set HDT_HOST_BINARY to the absolute path of the built Rust host.");
        return host;
    }

    // Linux parent-death signals follow the creating native thread. Keep that
    // thread alive through the exact child's wait, rather than borrowing a pool
    // thread that may retire while a long-lived raster child is still running.
    // Both callers run on their existing background actors, never the dispatcher.
    internal sealed class OwnedNativeChild : IDisposable
    {
        private readonly Thread _parent;
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Process Child { get; }
        public OwnedNativeChild(ProcessStartInfo start)
        {
            start.Environment["HDT_OWNED_PARENT_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var started = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
            _parent = new Thread(() =>
            {
                try
                {
                    var child = Process.Start(start) ?? throw new IOException("Owned native child could not start.");
                    started.SetResult(child);
                    child.WaitForExit();
                    _completed.SetResult();
                }
                catch (Exception error) { started.TrySetException(error); _completed.TrySetException(error); }
            })
            { IsBackground = true, Name = "HDT owned native child" };
            _parent.Start();
            try { Child = started.Task.GetAwaiter().GetResult(); }
            catch { _parent.Join(); _ = _completed.Task.Exception; throw; }
        }
        public void Dispose()
        {
            _parent.Join();
            try { _completed.Task.GetAwaiter().GetResult(); }
            finally { Child.Dispose(); }
        }
    }

    private static async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments, int maximum, CancellationToken cancellation, string? standardInput)
    {
        if (maximum is < 1 or > ReplayBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        var host = HostBinary();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Deadline);
        var acquired = false;
        Process? child = null;
        OwnedNativeChild? owned = null;
        try
        {
            await Slot.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = standardInput is not null, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            owned = new(start); child = owned.Child;
            var output = ReadPipeAsync(child.StandardOutput.BaseStream, maximum, deadline.Token);
            var error = ReadPipeAsync(child.StandardError.BaseStream, 4096, deadline.Token);
            var input = standardInput is null ? Task.CompletedTask : WriteInputAsync(child, standardInput, deadline.Token);
            await Task.WhenAll(output, error, input, child.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (child.ExitCode == 2) throw new FileNotFoundException("Snapshot file does not exist.");
            if (child.ExitCode != 0)
                throw new IOException($"Snapshot reader rejected input: {Encoding.UTF8.GetString(await error.ConfigureAwait(false)).Trim()}");
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new IOException("Snapshot read exceeded its two-second deadline; current state is unavailable."); }
        catch (System.ComponentModel.Win32Exception error)
        { throw new IOException("Rust snapshot reader could not start.", error); }
        finally
        {
            try
            {
                if (child is not null)
                {
                    if (!child.HasExited)
                    {
                        try { child.Kill(entireProcessTree: true); }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                        // Retain custody and the actor slot until the exact child exits.
                        // A kernel-stalled child must not create an unbounded chain of replacements.
                        _ = ReapAsync(owned!);
                        acquired = false;
                    }
                    else owned!.Dispose();
                }
            }
            finally { if (acquired) Slot.Release(); }
        }
    }
    private static async Task WriteInputAsync(Process child, string input, CancellationToken cancellation)
    {
        if (Encoding.UTF8.GetByteCount(input) > 65536) throw new IOException("Native request exceeds its 64 KiB bound.");
        await child.StandardInput.WriteAsync(input.AsMemory(), cancellation).ConfigureAwait(false);
        child.StandardInput.Close();
    }
    private static async Task ReapAsync(OwnedNativeChild owned)
    {
        try { try { await owned.Child.WaitForExitAsync().ConfigureAwait(false); } finally { owned.Dispose(); } }
        catch (Exception error) { Console.Error.WriteLine($"Owned native child reap failed: {error}"); }
        finally { Slot.Release(); }
    }
    private static async Task<byte[]> ReadPipeAsync(Stream stream, int maximum, CancellationToken cancellation)
    {
        var buffer = new byte[maximum + 1];
        var used = 0;
        while (used < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(used), cancellation).ConfigureAwait(false);
            if (count == 0) return buffer[..used];
            used += count;
        }
        throw new IOException("Snapshot reader output exceeds its byte bound.");
    }
}
