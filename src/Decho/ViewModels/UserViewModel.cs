using Avalonia.Media;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.ViewModels;

public sealed class UserViewModel(UserPresenceDto dto) : ViewModelBase
{
    private UserStatus _status = dto.Status;
    public string Username { get; } = dto.Username;
    public string DisplayName { get; } = dto.DisplayName ?? dto.Username;
    public string? NicknameColor { get; } = dto.NicknameColor;
    public string? StatusMessage { get; } = dto.StatusMessage;
    public ServerRole Role { get; } = dto.Role;

    public UserStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(StatusColor));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public string FullName => DisplayName ?? Username;

    public IBrush? DisplayColor
    {
        get
        {
            if (string.IsNullOrEmpty(NicknameColor))
            {
                return Brushes.White;
            }

            try
            {
                return new SolidColorBrush(Color.Parse(NicknameColor));
            }
            catch
            {
                return null;
            }
        }
    }

    public string StatusColor => Status switch
    {
        UserStatus.Online => "#4CAF50",
        UserStatus.Away => "#FF9800",
        UserStatus.DoNotDisturb => "#E53935",
        UserStatus.Invisible => "#9E9E9E",
        _ => "#9E9E9E",
    };

    public string StatusText => Status switch
    {
        UserStatus.Online => "Online",
        UserStatus.Away => "Away",
        UserStatus.DoNotDisturb => "Do Not Disturb",
        UserStatus.Invisible => "Invisible",
        _ => "Offline",
    };
}