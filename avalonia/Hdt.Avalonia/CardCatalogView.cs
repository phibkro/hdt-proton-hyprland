using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Hdt.Avalonia;

public sealed record CatalogCard(uint DbfId, string Id, string Name, string? CardType, string? CardClass, string[] Classes, uint? Cost, bool Collectible)
{
    public string ClassLabel => Classes.Length > 0 ? string.Join("/", Classes) : CardClass ?? "unknown class";
    public override string ToString() => $"{(Cost is null ? "?" : Cost)} mana • {Name} • {ClassLabel} • {CardType ?? "unknown type"} • {Id} / #{DbfId}";
}
public sealed record CatalogResult(byte SchemaVersion, int CatalogCards, int Matches, CatalogCard[] Cards);
public enum CardEdit { AddCopy, RemoveCopy }

public sealed class CardCatalogView : UserControl
{
    private readonly CancellationToken _lifetime;
    private bool _working;
    private readonly TextBlock _status = new() { Text = "Optional: supply a localized HearthstoneJSON card array. Nothing is downloaded or bundled.", TextWrapping = TextWrapping.Wrap };
    public TextBox PathBox { get; } = new() { MaxLength = 4096, PlaceholderText = "Local card catalog JSON path" };
    public TextBox QueryBox { get; } = new() { MaxLength = 256, PlaceholderText = "Card name, class, type, card ID or DBF ID" };
    public CheckBox IncludeNonCollectible { get; } = new() { Content = "Include noncollectible cards" };
    public ListBox Results { get; } = new() { MinHeight = 80, MaxHeight = 200 };
    public Button SearchButton { get; } = new() { Content = "Search local catalog" };
    public Button ChooseButton { get; } = new() { Content = "Choose catalog file…" };
    public Button AddButton { get; } = new() { Content = "Add selected card" };
    public Button RemoveButton { get; } = new() { Content = "Remove one copy" };
    public Button HeroButton { get; } = new() { Content = "Use selected hero" };
    public Task PendingSearch { get; private set; } = Task.CompletedTask;
    public string Status => _status.Text ?? "";
    public event Action<CatalogCard, CardEdit>? ChangeCard;
    public event Action<CatalogCard>? UseHero;
    public CardCatalogView(CancellationToken lifetime)
    {
        _lifetime = lifetime;
        SearchButton.Click += (_, _) => { if (!_working) PendingSearch = SearchAsync(); };
        ChooseButton.Click += (_, _) => { if (!_working) PendingSearch = ChooseAsync(); };
        AddButton.Click += (_, _) => { if (!_working && Results.SelectedItem is CatalogCard card) ChangeCard?.Invoke(card, CardEdit.AddCopy); };
        RemoveButton.Click += (_, _) => { if (!_working && Results.SelectedItem is CatalogCard card) ChangeCard?.Invoke(card, CardEdit.RemoveCopy); };
        HeroButton.Click += (_, _) => { if (!_working && Results.SelectedItem is CatalogCard { CardType: "HERO" } card) UseHero?.Invoke(card); };
        Results.SelectionChanged += (_, _) => Availability();
        PathBox.TextChanged += (_, _) => { Results.ItemsSource = null; _status.Text = "Catalog path changed; search to load it."; Availability(); };
        Content = new Expander
        {
            Header = "Find cards by name (optional local catalog)",
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { ChooseButton, PathBox, QueryBox, IncludeNonCollectible, SearchButton, Results,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AddButton, RemoveButton, HeroButton } }, _status }
            }
        };
        Availability();
    }
    private async Task ChooseAsync()
    {
        _working = true; Availability();
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new IOException("File chooser is unavailable.");
            var files = await provider.OpenFilePickerAsync(new() { Title = "Choose a local HearthstoneJSON catalog", AllowMultiple = false });
            if (files.Count == 1) PathBox.Text = files[0].TryGetLocalPath() ?? throw new IOException("Catalog requires a local regular file.");
        }
        catch (Exception error) { _status.Text = $"Catalog selection unavailable: {error.Message}"; }
        finally { _working = false; Availability(); }
    }
    private async Task SearchAsync()
    {
        _working = true; Results.ItemsSource = null; Availability();
        try
        {
            if (string.IsNullOrWhiteSpace(PathBox.Text)) throw new IOException("Choose an explicit local catalog file.");
            var request = System.Text.Json.JsonSerializer.Serialize(new { query = QueryBox.Text ?? "", includeNonCollectible = IncludeNonCollectible.IsChecked == true });
            var result = await SnapshotFiles.InvokeJsonAsync<CatalogResult>(["catalog", Path.GetFullPath(PathBox.Text)], 1024 * 1024, _lifetime, request);
            if (result.SchemaVersion != 1 || result.Cards is null || result.Cards.Length > 100 || result.CatalogCards is < 1 or > 50000
                || result.Matches < result.Cards.Length || result.Cards.Any(card => card is null || card.DbfId == 0 || card.Classes is null || string.IsNullOrWhiteSpace(card.Name)))
                throw new InvalidDataException("Unsupported catalog response.");
            Results.ItemsSource = result.Cards;
            _status.Text = $"{result.Matches} matches • showing {result.Cards.Length} • {result.CatalogCards} catalog entries. Metadata does not establish collection ownership or deck legality.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { _status.Text = $"Catalog unavailable: {error.Message}"; }
        finally { _working = false; Availability(); }
    }
    private void Availability()
    {
        PathBox.IsEnabled = QueryBox.IsEnabled = IncludeNonCollectible.IsEnabled = SearchButton.IsEnabled = ChooseButton.IsEnabled = !_working;
        AddButton.IsEnabled = RemoveButton.IsEnabled = !_working && Results.SelectedItem is CatalogCard;
        HeroButton.IsEnabled = !_working && Results.SelectedItem is CatalogCard { CardType: "HERO" };
    }
}
