namespace Decho.ViewModels;

public sealed class AutocompleteProvider(char trigger, Func<IEnumerable<string>> itemsSource, string insertPrefix, int maxResults = 10)
{
    public char Trigger { get; } = trigger;
    public Func<IEnumerable<string>> ItemsSource { get; } = itemsSource;
    public Func<string, string, bool> Filter { get; } = (item, filter) => item.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
    public Func<string, string> FormatInsertion { get; } = item => $"{insertPrefix}{item} ";
    public int MaxResults { get; } = maxResults;
}