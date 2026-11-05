using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Decho.Views;

public sealed partial class TextPromptWindow : Window
{
    private readonly bool _allowEmpty;

    /// <summary>The trimmed text, or null when the window was cancelled.</summary>
    public string? Result { get; private set; }

    public TextPromptWindow()
    {
        InitializeComponent();
    }

    public TextPromptWindow(string title, string description, string watermark, string confirmText, string? initialText = null, int maxLength = 0, bool allowEmpty = false)
        : this()
    {
        _allowEmpty = allowEmpty;
        Title = title;
        DescriptionText.Text = description;
        InputBox.Watermark = watermark;
        InputBox.Text = initialText;
        InputBox.MaxLength = maxLength;
        ConfirmButton.Content = confirmText;
        Opened += (_, _) => InputBox.Focus();
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        string text = InputBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && !_allowEmpty)
        {
            _ = InputBox.Focus();
            return;
        }

        Result = text;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
