using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Decho.ViewModels;

public sealed class AutocompleteController(IEnumerable<AutocompleteProvider> providers) : ViewModelBase
{
    private static readonly Regex TriggerPattern = new(@"([@#])(\w*)$", RegexOptions.Compiled);
    private readonly List<AutocompleteProvider> _providers = providers.ToList();
    public ObservableCollection<string> FilteredItems { get; } = [];

    public bool ShowPopup
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int SelectedIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string FilterText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public AutocompleteProvider? ActiveProvider { get; private set; }

    public int TriggerCharIndex { get; private set; }

    public void Update(string text)
    {
        if (string.IsNullOrEmpty(text) || _providers.Count == 0)
        {
            Reset();
            return;
        }

        Match match = TriggerPattern.Match(text);
        if (!match.Success)
        {
            Reset();
            return;
        }

        char trigger = match.Groups[1].Value[0];
        AutocompleteProvider? provider = _providers.FirstOrDefault(p => p.Trigger == trigger);
        if (provider is null)
        {
            Reset();
            return;
        }

        string filter = match.Groups[2].Value;
        ActiveProvider = provider;
        FilterText = filter;
        TriggerCharIndex = match.Index;

        List<string> items = provider.ItemsSource().ToList();
        List<string> filtered = items
            .Where(item => provider.Filter(item, filter))
            .Take(provider.MaxResults)
            .ToList();

        SelectedIndex = -1;
        FilteredItems.Clear();
        foreach (string item in filtered)
        {
            FilteredItems.Add(item);
        }

        ShowPopup = FilteredItems.Count > 0;
        SelectedIndex = 0;
    }

    public string? GetInsertion(string item)
    {
        if (ActiveProvider is null)
        {
            return null;
        }

        return ActiveProvider.FormatInsertion(item);
    }

    public void Reset()
    {
        ShowPopup = false;
        ActiveProvider = null;
        FilterText = string.Empty;
        FilteredItems.Clear();
    }
}