using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Decho.Services;

using Splat;

namespace Decho.Views;

public partial class AvatarView : UserControl
{
    public static readonly StyledProperty<string?> UsernameProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(Username));

    public static readonly StyledProperty<string?> ServerUrlProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(ServerUrl));

    public static readonly StyledProperty<string?> FallbackNameProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(FallbackName));

    private readonly IAvatarService? _avatarService = Locator.Current.GetService<IAvatarService>();
    private int _loadVersion;
    private bool _isAttached;

    public string? Username
    {
        get => GetValue(UsernameProperty);
        set => SetValue(UsernameProperty, value);
    }

    public string? ServerUrl
    {
        get => GetValue(ServerUrlProperty);
        set => SetValue(ServerUrlProperty, value);
    }

    public string? FallbackName
    {
        get => GetValue(FallbackNameProperty);
        set => SetValue(FallbackNameProperty, value);
    }

    public AvatarView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => InitialText.FontSize = Math.Max(args.NewSize.Height * 0.45, 1);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;

        if (_avatarService is not null)
        {
            _avatarService.AvatarChanged += OnAvatarChanged;
        }

        _ = ReloadAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;

        if (_avatarService is not null)
        {
            _avatarService.AvatarChanged -= OnAvatarChanged;
        }

        _loadVersion++;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == UsernameProperty || change.Property == FallbackNameProperty)
        {
            UpdateInitial();
        }

        if ((change.Property == UsernameProperty || change.Property == ServerUrlProperty) && _isAttached)
        {
            // Item containers get recycled, so the previous user's picture must not linger.
            SetPicture(null);
            _ = ReloadAsync();
        }
    }

    private void OnAvatarChanged(string serverUrl, string username)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.Equals(serverUrl, ServerUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(username, Username, StringComparison.OrdinalIgnoreCase)
                && _isAttached)
            {
                _ = ReloadAsync();
            }
        });
    }

    private async Task ReloadAsync()
    {
        int version = ++_loadVersion;

        if (_avatarService is null || string.IsNullOrEmpty(ServerUrl) || string.IsNullOrEmpty(Username))
        {
            SetPicture(null);
            return;
        }

        Bitmap? bitmap = await _avatarService.GetAvatarAsync(ServerUrl, Username);
        if (version == _loadVersion)
        {
            SetPicture(bitmap);
        }
    }

    private void SetPicture(Bitmap? bitmap)
    {
        Picture.Fill = bitmap is null ? null : new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        Picture.IsVisible = bitmap is not null;
    }

    private void UpdateInitial()
    {
        string? name = string.IsNullOrWhiteSpace(FallbackName) ? Username : FallbackName;
        InitialText.Text = string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : char.ToUpperInvariant(name.TrimStart()[0]).ToString();
    }
}
