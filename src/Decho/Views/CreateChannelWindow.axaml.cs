using Avalonia.Controls;

namespace Decho.Views;

public partial class CreateChannelWindow : Window
{
    public string? ResultName { get; private set; }
    public string? ResultTopic { get; private set; }
    public bool ResultIsPublic { get; private set; } = true;
    public string? ResultPassword { get; private set; }

    public CreateChannelWindow()
    {
        InitializeComponent();
    }

    private void OnCreateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = ChannelName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ChannelName.Focus();
            return;
        }

        ResultName = name;
        ResultTopic = string.IsNullOrWhiteSpace(ChannelTopic.Text) ? null : ChannelTopic.Text.Trim();
        ResultIsPublic = PublicCheckBox.IsChecked == true;
        ResultPassword = string.IsNullOrWhiteSpace(ChannelPassword.Text) ? null : ChannelPassword.Text;
        Close(true);
    }
}
