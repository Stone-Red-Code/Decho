using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Decho.Views;

public sealed partial class ModerationWindow : Window
{
    private static readonly MuteDuration[] MuteDurations =
    [
        new("10 minutes", 10),
        new("1 hour", 60),
        new("1 day", 1440),
        new("1 week", 10080),
        new("Until unmuted", null),
    ];

    public string? ResultUsername { get; private set; }

    public string? ResultReason { get; private set; }

    public int? ResultDurationMinutes { get; private set; }

    public ModerationWindow()
    {
        InitializeComponent();
    }

    private ModerationWindow(string title, string description, string confirmText)
        : this()
    {
        Title = title;
        DescriptionText.Text = description;
        ConfirmButton.Content = confirmText;
    }

    public static ModerationWindow ForReason(string title, string description, string confirmText)
    {
        ModerationWindow window = new ModerationWindow(title, description, confirmText);
        window.ReasonBox.IsVisible = true;
        return window;
    }

    public static ModerationWindow ForMute(string username)
    {
        ModerationWindow window = new ModerationWindow($"Mute {username}", $"{username} won't be able to send messages or upload files.", "Mute");
        window.DurationBox.IsVisible = true;
        window.DurationBox.ItemsSource = MuteDurations;
        window.DurationBox.SelectedIndex = 1;
        return window;
    }

    public static ModerationWindow ForUsername(string title, string description, string confirmText)
    {
        ModerationWindow window = new ModerationWindow(title, description, confirmText);
        window.UsernameBox.IsVisible = true;
        window.Opened += (_, _) => window.UsernameBox.Focus();
        return window;
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text?.Trim().TrimStart('@') ?? string.Empty;
        if (UsernameBox.IsVisible && username.Length == 0)
        {
            _ = UsernameBox.Focus();
            return;
        }

        ResultUsername = username;
        ResultReason = string.IsNullOrWhiteSpace(ReasonBox.Text) ? null : ReasonBox.Text.Trim();
        ResultDurationMinutes = (DurationBox.SelectedItem as MuteDuration)?.Minutes;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private sealed record MuteDuration(string Label, int? Minutes)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
