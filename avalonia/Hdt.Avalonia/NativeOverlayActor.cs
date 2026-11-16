using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Hdt.Avalonia;

public sealed record GameWindowChoice(uint Window, uint Pid, string Class, int X, int Y, int Width, int Height)
{
    public override string ToString() => $"Hearthstone • {Width}×{Height} at {X},{Y} • {Class}";
}
internal enum OverlayReplyKind { Unknown, Ready, Status }
internal sealed record OverlayReply(OverlayReplyKind Kind, int SchemaVersion, uint Window, int? Pid, int? Width,
    int? Height, string? PixelFormat, int? InputRectangles, bool? Visible);

// A separate long-lived owner from the one-shot file reader. One pending frame,
// bounded pipes/deadlines, and custody retained until the exact child is reaped.
public sealed class NativeOverlayActor
{
    private static readonly JsonSerializerOptions ReplyOptions = new(ReplaySource.JsonOptions)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<OverlayReplyKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };
    private readonly CancellationTokenSource _lifetime;
    private readonly Channel<OverlayRaster?> _frames = Channel.CreateBounded<OverlayRaster?>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly TaskCompletionSource<OverlaySpec> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string> _status;
    public Task<OverlaySpec> Ready => _ready.Task;
    public Task Completion { get; }
    public NativeOverlayActor(GameWindowChoice game, Action<string> status, CancellationToken lifetime = default)
    {
        _status = status; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Completion = Task.Run(() => RunAsync(game));
    }
    public void Offer(OverlayRaster? frame) => _frames.Writer.TryWrite(frame);
    public void Stop() { try { _lifetime.Cancel(); } catch (ObjectDisposedException) { } }
    public async Task StopAsync() { Stop(); await Completion; }
    private async Task RunAsync(GameWindowChoice game)
    {
        Process? child = null;
        var tasks = new List<Task>();
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(SnapshotFiles.HostBinary())
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false)
            };
            foreach (var value in new[] { "native-overlay", "run", game.Window.ToString(System.Globalization.CultureInfo.InvariantCulture), game.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), game.Class })
                start.ArgumentList.Add(value);
            child = Process.Start(start) ?? throw new IOException("Native overlay owner could not start.");
            var diagnostics = ReadDiagnosticsAsync(child.StandardError.BaseStream, _lifetime.Token); tasks.Add(diagnostics);
            var hello = await ReadReplyAsync(child.StandardOutput.BaseStream, _lifetime.Token);
            if (hello.Kind != OverlayReplyKind.Ready || hello.SchemaVersion != 1 || hello.Window == 0 || hello.Pid != child.Id
                || hello.InputRectangles != 0 || hello.Width is null || hello.Height is null || hello.PixelFormat is null)
                throw new InvalidDataException("Native overlay did not prove input-empty preparation.");
            var spec = new OverlaySpec(hello.SchemaVersion, hello.Width.Value, hello.Height.Value, hello.PixelFormat); spec.Validate();
            _ready.TrySetResult(spec);
            _status("Native overlay prepared; return to the selected Hearthstone window.");
            var writer = WriteFramesAsync(child, spec, _lifetime.Token); tasks.Add(writer);
            var reader = ReadStatusAsync(child.StandardOutput.BaseStream, hello.Window, _lifetime.Token); tasks.Add(reader);
            var exited = child.WaitForExitAsync(); tasks.Add(exited);
            var ended = await Task.WhenAny(diagnostics, writer, reader, exited);
            await ended;
            if (!_lifetime.IsCancellationRequested) throw new IOException("Native overlay owner exited or closed its stream.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _ready.TrySetCanceled(); }
        catch (Exception error)
        {
            var failure = new IOException($"Native overlay unavailable: {error.Message}", error);
            _ready.TrySetException(failure); _status(failure.Message);
        }
        finally
        {
            _frames.Writer.TryComplete(); _lifetime.Cancel();
            if (child is not null)
            {
                if (!child.HasExited)
                    try { child.Kill(entireProcessTree: true); }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                // Never release the controller's owner while this child still exists.
                await child.WaitForExitAsync();
                try { await Task.WhenAll(tasks); } catch (Exception) { /* Primary failure already surfaced; pipes close on owned exit. */ }
                child.Dispose();
            }
            _ready.TrySetCanceled(); _lifetime.Dispose();
        }
    }
    private async Task WriteFramesAsync(Process child, OverlaySpec spec, CancellationToken lifetime)
    {
        await foreach (var frame in _frames.Reader.ReadAllAsync(lifetime))
        {
            object packet;
            if (frame is null) packet = new { kind = "hide" };
            else
            {
                if (frame.Pixels.Length != checked(spec.Width * spec.Height * 4)) throw new InvalidDataException("Incomplete native raster frame.");
                packet = new
                {
                    kind = "frame",
                    schemaVersion = 1,
                    sourceSession = frame.SourceSession,
                    sequence = frame.Sequence,
                    observedMs = frame.ObservedMs,
                    width = spec.Width,
                    height = spec.Height,
                    pixelFormat = spec.PixelFormat,
                    pixels = Convert.ToBase64String(frame.Pixels)
                };
            }
            var json = JsonSerializer.Serialize(packet);
            if (Encoding.UTF8.GetByteCount(json) + 1 > 2 * 1024 * 1024) throw new InvalidDataException("Native raster request exceeds line bound.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime); deadline.CancelAfter(TimeSpan.FromSeconds(2));
            await child.StandardInput.WriteLineAsync(json.AsMemory(), deadline.Token); await child.StandardInput.FlushAsync(deadline.Token);
        }
    }
    private async Task ReadStatusAsync(Stream stream, uint window, CancellationToken lifetime)
    {
        while (true)
        {
            var reply = await ReadReplyAsync(stream, lifetime);
            if (reply.Kind != OverlayReplyKind.Status || reply.SchemaVersion != 1 || reply.Window != window || reply.InputRectangles != 0 || reply.Visible is null)
                throw new InvalidDataException("Native overlay status lost its owned/input-empty identity.");
            _status(reply.Visible.Value ? "Native overlay displayed • game input passes through." : "Native overlay hidden while the game/source is inactive.");
        }
    }
    private static async Task<OverlayReply> ReadReplyAsync(Stream stream, CancellationToken lifetime)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var bytes = new byte[8192]; var single = new byte[1]; var used = 0;
        while (used < bytes.Length)
        {
            if (await stream.ReadAsync(single, deadline.Token) == 0) throw new IOException("Native overlay status stream closed.");
            if (single[0] == (byte)'\n') return JsonSerializer.Deserialize<OverlayReply>(bytes.AsSpan(0, used), ReplyOptions)
                ?? throw new InvalidDataException("Empty native overlay reply.");
            bytes[used++] = single[0];
        }
        throw new IOException("Native overlay reply exceeds its byte bound.");
    }
    private static async Task ReadDiagnosticsAsync(Stream stream, CancellationToken lifetime)
    {
        var buffer = new byte[4097]; var used = 0;
        while (used < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(used), lifetime);
            if (count == 0) return;
            used += count;
        }
        throw new IOException("Native overlay diagnostics exceed their byte bound.");
    }
}
