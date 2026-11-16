using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hdt.Avalonia;

public sealed record OverlaySpec(int SchemaVersion, int Width, int Height, string PixelFormat)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || Width is < 1 or > 1024 || Height is < 1 or > 1024
            || (long)Width * Height * 4 > 2 * 1024 * 1024 || PixelFormat != "bgra8888Opaque")
            throw new InvalidDataException("Unsupported bounded native overlay raster specification.");
    }
}
public abstract record OverlayUpdate(byte[] Pixels);
public sealed record OverlayRaster(string SourceSession, int Sequence, long ObservedMs, byte[] Pixels) : OverlayUpdate(Pixels);
public sealed record OverlayNotice(string RendererSession, int Sequence, long RenderedMs, byte[] Pixels) : OverlayUpdate(Pixels);
public static class OverlayRendering
{
    public static OverlayRaster Render(NativeObservation value, TrackerSettings settings, OverlaySpec spec, string? capture = null)
    {
        spec.Validate();
        var content = new StackPanel { Spacing = 7 };
        TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(Text("Native deck tracker", 19));
        content.Children.Add(Text($"{value.Mode} • {value.MatchMode}", 13));
        if (value.Mode == NativeMode.Arena && value.Arena is { } arena)
        {
            content.Children.Add(Text(ArenaPresentation.Heading(arena), 14));
            if (arena.ChoiceEvidence is { } evidence) content.Children.Add(Text(ArenaPresentation.Evidence(evidence), 11));
            foreach (var choice in ArenaPresentation.Choices(arena, settings, 16).Take(3)) content.Children.Add(choice);
            if (arena.CardOffers.Length > 3) content.Children.Add(Text($"{arena.CardOffers.Length - 3} more offers in the native app."));
        }
        else
        {
            content.Children.Add(Text($"Your health {value.PlayerHealth?.ToString() ?? "unknown"} • hand {value.PlayerHandCount?.ToString() ?? "unknown"}", 14));
            var cards = value.Mode == NativeMode.Battlegrounds ? value.PlayerBoard! : value.RemainingDeck!;
            content.Children.Add(Text(value.Mode == NativeMode.Battlegrounds ? "Your observed board" : "Observed remaining deck", 16));
            foreach (var card in cards.Take(22))
                content.Children.Add(new TextBlock
                {
                    Text = card.ToString(),
                    Foreground = Brushes.White,
                    Opacity = card.Count == 0 ? 0.5 : 1,
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                });
            if (cards.Length > 22) content.Children.Add(Text($"{cards.Length - 22} more entries in the native app."));
            if (cards.Length == 0) content.Children.Add(Text("No cards observed."));
        }
        content.Children.Add(Text($"Updated {value.ObservedAt.ToLocalTime():T} • game input passes through", 11));
        var root = new Border
        {
            Width = spec.Width,
            Height = spec.Height,
            Padding = new(12),
            Background = new SolidColorBrush(Color.Parse("#182831")),
            Child = content
        };
        root.Measure(new Size(spec.Width, spec.Height)); root.Arrange(new Rect(0, 0, spec.Width, spec.Height)); root.UpdateLayout();
        using var image = new RenderTargetBitmap(new PixelSize(spec.Width, spec.Height), new Vector(96, 96));
        image.Render(root);
        if (capture is not null) image.Save(capture, PngBitmapEncoderOptions.Default);
        using var buffer = new PixelBuffer(spec.Width, spec.Height);
        image.CopyPixels(buffer);
        return new(value.SessionId, value.Sequence, value.ObservedAt.ToUnixTimeMilliseconds(), buffer.Pixels);
    }
    public static OverlayNotice Notice(string rendererSession, int sequence, OverlaySpec spec, string heading, string explanation)
    {
        spec.Validate();
        var content = new Border
        {
            Width = spec.Width,
            Height = spec.Height,
            Padding = new(14),
            Background = new SolidColorBrush(Color.Parse("#182831")),
            Child = new StackPanel
            {
                Spacing = 14,
                Children = {
                new TextBlock { Text = "Native deck tracker", FontSize = 19, Foreground = Brushes.White },
                new TextBlock { Text = heading, FontSize = 18, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = explanation, FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Previous game advice cleared. Waiting for fresh validated observations.", FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Independent renderer • game input passes through", FontSize = 11, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } }
            }
        };
        content.Measure(new(spec.Width, spec.Height)); content.Arrange(new(0, 0, spec.Width, spec.Height)); content.UpdateLayout();
        using var image = new RenderTargetBitmap(new(spec.Width, spec.Height), new(96, 96)); image.Render(content);
        using var buffer = new PixelBuffer(spec.Width, spec.Height); image.CopyPixels(buffer);
        return new(rendererSession, sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), buffer.Pixels);
    }
    private sealed class PixelBuffer : ILockedFramebuffer
    {
        private GCHandle _pin;
        public byte[] Pixels { get; }
        public PixelBuffer(int width, int height)
        { Size = new(width, height); RowBytes = checked(width * 4); Pixels = new byte[checked(RowBytes * height)]; _pin = GCHandle.Alloc(Pixels, GCHandleType.Pinned); }
        public IntPtr Address => _pin.AddrOfPinnedObject();
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Opaque;
        public void Dispose() { if (_pin.IsAllocated) _pin.Free(); }
    }
}
public static class ArenaPresentation
{
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    public static string Evidence(ArenaChoiceEvidence evidence) => $"HDT watcher event received {evidence.ReceivedAt:HH:mm:ss} UTC • memory freshness unknown";
    public static string Heading(ArenaObservation frame)
    {
        var phase = frame.ViewSelection switch
        {
            ArenaHeroSelection => "Choose a hero",
            ArenaCardDraft => frame.SchemaVersion == 3 ? "Card draft • pick progress unavailable" : $"Card draft • {frame.PickIndex} cards picked",
            ArenaUnavailable => "Selection unavailable",
            _ => "Arena menu • no active choices"
        };
        return $"{(frame.Underground ? "Underground Arena" : "Arena")} • {phase}{(frame.ViewSelection is ArenaHeroSelection ? "" : $" • {frame.Hero}")}";
    }
    public static IEnumerable<Control> Choices(ArenaObservation frame, TrackerSettings settings, double size)
    {
        switch (frame.ViewSelection)
        {
            case ArenaHeroSelection heroes:
                foreach (var hero in heroes.Choices)
                    yield return TrackerLayout.Section(hero.Name, new StackPanel
                    {
                        Spacing = 6,
                        Children = {
                        Text(hero.Class ?? "Class not observed", size - 3), Text(hero.Id, 12),
                        Text(hero.SourceIndex is { } index ? $"Source actor index {index} • position unknown" : "Source position not supplied", 11),
                        Text("Hero statistics unavailable", 13) }
                    });
                if (heroes.UnavailableReason is not null) yield return Text(heroes.UnavailableReason, 14);
                break;
            case ArenaCardDraft cards:
                foreach (var offer in cards.Choices) yield return Offer(offer, frame.PickedDeck, settings, frame.Underground, size, frame.SchemaVersion != 3);
                break;
            case ArenaUnavailable unavailable:
                yield return Text($"{unavailable.Kind} choices unavailable • {unavailable.Reason}", 14);
                break;
        }
        if (frame.ChoiceEvidence is { } evidence) yield return Text(Evidence(evidence), 12);
    }
    public static Border Offer(ArenaOffer offer, ArenaCard[] deck, TrackerSettings settings, bool underground, double size, bool adviceAvailable = true)
    {
        var lines = new StackPanel { Spacing = 6, Children = { Text($"{offer.Card.Mana} mana   {offer.Card.Name}", size), Text(offer.Card.Id, 12) } };
        if (offer.SourceIndex is { } index) lines.Children.Add(Text($"Source actor index {index} • position unknown", 11));
        if (!adviceAvailable) lines.Children.Add(Text("Ratings and deck synergies unavailable • no matching source generation", 13));
        else
        {
            if (settings.ShowArenasmithScore) lines.Children.Add(Text($"Arenasmith {offer.DisplayScore} • plaque {offer.Plaque}", size - 3));
            if (settings.ShowArenaDeckSynergies) lines.Children.Add(Text($"Deck synergies: {offer.SynergyCount(deck)}", 14));
        }
        return new Border
        {
            Padding = new(size < 20 ? 10 : 16),
            CornerRadius = new(6),
            BorderThickness = new(2),
            BorderBrush = new SolidColorBrush(Color.Parse(underground ? "#932020" : "#067F93")),
            Child = lines
        };
    }
}
