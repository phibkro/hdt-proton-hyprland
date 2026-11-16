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
            return JsonSerializer.Deserialize<T>(bytes, ReplaySource.JsonOptions)
                ?? throw new InvalidDataException("Native response must not be null.");
        }, cancellation);

    private static async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments, int maximum, CancellationToken cancellation, string? standardInput)
    {
        if (maximum is < 1 or > ReplayBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        var host = Environment.GetEnvironmentVariable("HDT_HOST_BINARY");
        if (string.IsNullOrWhiteSpace(host) || !Path.IsPathFullyQualified(host))
            throw new IOException("Set HDT_HOST_BINARY to the absolute path of the built Rust host.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Deadline);
        var acquired = false;
        Process? child = null;
        try
        {
            await Slot.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = standardInput is not null, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            child = Process.Start(start) ?? throw new IOException("Snapshot reader could not start.");
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
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    try { child.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    // Retain custody and the actor slot until the exact child exits.
                    // A kernel-stalled child must not create an unbounded chain of replacements.
                    _ = ReapAsync(child);
                    acquired = false;
                }
                else child.Dispose();
            }
            if (acquired) Slot.Release();
        }
    }
    private static async Task WriteInputAsync(Process child, string input, CancellationToken cancellation)
    {
        if (Encoding.UTF8.GetByteCount(input) > 65536) throw new IOException("Native request exceeds its 64 KiB bound.");
        await child.StandardInput.WriteAsync(input.AsMemory(), cancellation).ConfigureAwait(false);
        child.StandardInput.Close();
    }
    private static async Task ReapAsync(Process child)
    {
        try { await child.WaitForExitAsync().ConfigureAwait(false); }
        finally { child.Dispose(); Slot.Release(); }
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
