using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Hdt.Avalonia;

public sealed record CatalogCard(uint DbfId, string Id, string Name, string? CardType, string? CardClass, string[] Classes, uint? Cost, bool Collectible)
{
    public override string ToString() => CatalogSurface.CardLabel(DbfId, Id, Name, CardType, CardClass, Classes, Cost);
}
public sealed record CatalogResult(byte SchemaVersion, int CatalogCards, int Matches, CatalogCard[] Cards);
public enum CardEdit { AddCopy, RemoveCopy }

public sealed class CardCatalogView : CatalogSurface
{
    private readonly CancellationToken _lifetime;
    private bool _working;
    public Task PendingSearch { get; private set; } = Task.CompletedTask;
    public string Status => StatusText.Text ?? "";
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
        PathBox.TextChanged += (_, _) => { Results.ItemsSource = null; StatusText.Text = "Catalog path changed; search to load it."; Availability(); };
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
        catch (Exception error) { StatusText.Text = $"Catalog selection unavailable: {error.Message}"; }
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
            StatusText.Text = $"{result.Matches} matches • showing {result.Cards.Length} • {result.CatalogCards} catalog entries. Metadata does not establish collection ownership or deck legality.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { StatusText.Text = $"Catalog unavailable: {error.Message}"; }
        finally { _working = false; Availability(); }
    }
    private void Availability()
    {
        var selection = Results.SelectedItem is CatalogCard { CardType: "HERO" } ? CatalogSelection.Hero : Results.SelectedItem is CatalogCard ? CatalogSelection.Card : CatalogSelection.NoneSelected;
        ApplyAvailability(_working, selection);
    }
}
