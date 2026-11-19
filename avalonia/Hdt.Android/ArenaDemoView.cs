using System.Text.Json;
using System.Text.Json.Serialization;
using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hdt.Avalonia;
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Orientation = Avalonia.Layout.Orientation;

namespace Hdt.Mobile;

internal static class ArenaOverlayText
{
    public static string Format(IEnumerable<string> offers) => string.Join("\n", offers);
}

internal sealed class ArenaDemoView : UserControl
{
    private const string ScorePreference = "show-arena-scores";
    private const string SynergyPreference = "show-arena-synergies";
    private static readonly Lazy<ArenaObservation[]> Replay = new(LoadReplay);

    private readonly TextBlock heading = new() { FontSize = 22, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock metadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock overlay = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel offers = new() { Spacing = 6 };
    private readonly StackPanel picked = new() { Spacing = 4 };
    private readonly Button next = new() { Content = "Next observation" };
    private readonly Button reset = new() { Content = "Rewind" };
    private readonly CheckBox scores = new() { Content = "Show sample ratings" };
    private readonly CheckBox synergies = new() { Content = "Show sample synergies" };
    private int position;

    public ArenaDemoView()
    {
        using var preferences = OpenPreferences();
        scores.IsChecked = preferences?.GetBoolean(ScorePreference, true) ?? true;
        synergies.IsChecked = preferences?.GetBoolean(SynergyPreference, true) ?? true;
        scores.IsCheckedChanged += (_, _) => Save(ScorePreference, scores.IsChecked == true);
        synergies.IsCheckedChanged += (_, _) => Save(SynergyPreference, synergies.IsChecked == true);
        next.Click += (_, _) => { if (position + 1 < Replay.Value.Length) position++; Render(); };
        reset.Click += (_, _) => { position = 0; Render(); };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                MaxWidth = 540,
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    Label("Arena replay. Scores, picks and synergies are sample data."),
                    heading,
                    metadata,
                    new WrapPanel { Orientation = Orientation.Horizontal, Children = { reset, next } },
                    scores,
                    synergies,
                    new TextBlock { Text = "Offered cards", FontWeight = FontWeight.SemiBold },
                    offers,
                    new TextBlock { Text = "Picked deck", FontWeight = FontWeight.SemiBold },
                    picked,
                    new TextBlock { Text = "Live overlay preview", FontWeight = FontWeight.SemiBold },
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(24, 31, 42)),
                        Padding = new Thickness(8, 6),
                        MaxWidth = 180,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = overlay,
                    },
                },
            },
        };
        Render();
    }

    private static ArenaObservation[] LoadReplay()
    {
        using var stream = typeof(ArenaDemoView).Assembly.GetManifestResourceStream("Hdt.Mobile.DraftReplay.json")
            ?? throw new InvalidOperationException("The sample Arena replay is missing.");
        var context = new ArenaDemoJsonContext(ArenaReplay.JsonOptions);
        var frames = JsonSerializer.Deserialize(stream, context.ArenaObservationArray)
            ?? throw new InvalidDataException("The sample Arena replay is empty.");
        ArenaReplay.Validate(frames);
        return frames;
    }

    private static ISharedPreferences? OpenPreferences() =>
        global::Android.App.Application.Context.GetSharedPreferences("arena-demo", FileCreationMode.Private);

    private void Save(string key, bool value)
    {
        using var preferences = OpenPreferences();
        using var editor = preferences?.Edit();
        editor?.PutBoolean(key, value);
        editor?.Apply();
        Render();
    }

    private void Render()
    {
        var frames = Replay.Value;
        var frame = frames[position];
        heading.Text = frame.Description;
        metadata.Text = $"{frame.Hero} · {(frame.Underground ? "Underground" : "Arena")} · {frame.PickIndex} picked";
        reset.IsEnabled = position != 0;
        next.IsEnabled = position + 1 < frames.Length;
        offers.Children.Clear();
        foreach (var offer in frame.Offers)
        {
            var name = $"{offer.Card.Mana} mana · {offer.Card.Name}";
            if (scores.IsChecked == true)
                name += $" · Rating {offer.DisplayScore}";
            if (synergies.IsChecked == true)
                name += $" · Synergies {offer.SynergyCount(frame.PickedDeck)}";
            offers.Children.Add(Label(name));
        }
        if (frame.Offers.Length == 0)
            offers.Children.Add(Label("No offers"));

        picked.Children.Clear();
        foreach (var group in frame.PickedDeck.GroupBy(card => card.Id))
            picked.Children.Add(Label($"{group.First().Name} ×{group.Count()}"));
        if (frame.PickedDeck.Length == 0)
            picked.Children.Add(Label("No picks yet"));

        overlay.Text = frame.Offers.Length == 0
            ? "Overlay hidden"
            : ArenaOverlayText.Format(frame.Offers.Select(offer => offer.Card.Name));
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}


[JsonSerializable(typeof(ArenaObservation[]))]
internal sealed partial class ArenaDemoJsonContext : JsonSerializerContext
{
}
