using Avalonia.Controls;
using Avalonia.Interactivity;

using EchoHub.Core.Constants;

namespace Decho.Views;

public sealed partial class ChangePassphraseWindow : Window
{
    public string? ResultCurrentPassphrase { get; private set; }

    public string? ResultNewPassphrase { get; private set; }

    public ChangePassphraseWindow()
    {
        InitializeComponent();
    }

    public ChangePassphraseWindow(string channelName)
        : this()
    {
        DescriptionText.Text = $"Everyone who joins #{channelName} later, or on a new device, will need the new passphrase. Message history stays readable.";
        Opened += (_, _) => CurrentBox.Focus();
    }

    private void OnChangeClicked(object? sender, RoutedEventArgs e)
    {
        string current = CurrentBox.Text ?? string.Empty;
        string updated = NewBox.Text ?? string.Empty;

        string? error = current.Length == 0 ? "Enter the current passphrase."
            : updated.Length < ValidationConstants.MinChannelPasswordLength
                ? $"The new passphrase must be at least {ValidationConstants.MinChannelPasswordLength} characters."
            : updated.Length > ValidationConstants.MaxPasswordLength
                ? $"The new passphrase must not exceed {ValidationConstants.MaxPasswordLength} characters."
            : updated != ConfirmBox.Text ? "The new passphrases don't match."
            : null;

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        ResultCurrentPassphrase = current;
        ResultNewPassphrase = updated;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
