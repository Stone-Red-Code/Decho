namespace Decho.ViewModels;

public sealed class AutocompleteProvider
{
    public char Trigger { get; }
    public Func<IEnumerable<string>> ItemsSource { get; }
    public Func<string, string, bool> Filter { get; }
    public Func<string, string> FormatInsertion { get; }
    public int MaxResults { get; }

    public AutocompleteProvider(
        char trigger,
        Func<IEnumerable<string>> itemsSource,
        string insertPrefix,
        int maxResults = 10)
    {
        Trigger = trigger;
        ItemsSource = itemsSource;
        Filter = (item, filter) => item.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
        FormatInsertion = item => $"{insertPrefix}{item} ";
        MaxResults = maxResults;
    }
}