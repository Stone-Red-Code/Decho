using Avalonia.Controls;
using Avalonia.Interactivity;

using Decho.Services;

using EchoHub.Core.DTOs;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Decho.Views;

public sealed record InviteRow(string Code, string Details);

public sealed partial class InvitesWindow : Window
{
    private const int MaxUsesLimit = 1000;

    private static readonly InviteExpiry[] Expiries =
    [
        new("Never expires", null),
        new("Expires in 1 hour", 1),
        new("Expires in 1 day", 24),
        new("Expires in 1 week", 168),
        new("Expires in 30 days", 720),
    ];

    private readonly IInviteService? _inviteService;
    private readonly string _serverUrl = string.Empty;

    public InvitesWindow()
    {
        InitializeComponent();
    }

    public InvitesWindow(IInviteService inviteService, string serverUrl)
        : this()
    {
        _inviteService = inviteService;
        _serverUrl = serverUrl;
        ExpiryBox.ItemsSource = Expiries;
        ExpiryBox.SelectedIndex = 0;
        Opened += async (_, _) => await RunAsync(() => Task.CompletedTask);
    }

    private static string FormatDetails(InviteDto invite)
    {
        string state = invite.UseCount >= invite.MaxUses ? "used up"
            : invite.ExpiresAt is { } expired && expired <= DateTimeOffset.UtcNow ? "expired"
            : invite.ExpiresAt is { } expires ? $"expires {expires.ToLocalTime():g}"
            : "active";

        return $"{invite.UseCount}/{invite.MaxUses} used, {state}, by {invite.CreatedByUsername}";
    }

    /// <summary>Runs a change to the invites, then shows the resulting list or what went wrong.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        ErrorText.IsVisible = false;

        try
        {
            await action();
            List<InviteDto> invites = await _inviteService!.GetInvitesAsync(_serverUrl);
            InviteList.ItemsSource = invites.Select(i => new InviteRow(i.Code, FormatDetails(i))).ToList();
            EmptyText.IsVisible = invites.Count == 0;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private async void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxUsesBox.Text, out int maxUses) || maxUses is < 1 or > MaxUsesLimit)
        {
            ShowError($"Max uses must be a number from 1 to {MaxUsesLimit}.");
            return;
        }

        int? expiresInHours = (ExpiryBox.SelectedItem as InviteExpiry)?.Hours;
        await RunAsync(() => _inviteService!.CreateInviteAsync(_serverUrl, maxUses, expiresInHours));
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InviteRow row } && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(row.Code);
        }
    }

    private async void OnRevokeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InviteRow row })
        {
            return;
        }

        ButtonResult confirm = await MessageBoxManager
            .GetMessageBoxStandard("Revoke Invite", $"Revoke invite {row.Code}? It can't be used after this.", ButtonEnum.YesNo)
            .ShowWindowDialogAsync(this);
        if (confirm == ButtonResult.Yes)
        {
            await RunAsync(() => _inviteService!.RevokeInviteAsync(_serverUrl, row.Code));
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed record InviteExpiry(string Label, int? Hours)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
