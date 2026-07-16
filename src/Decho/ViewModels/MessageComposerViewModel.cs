using EchoHub.Client.Commands;

using System.Collections.ObjectModel;
using System.Reactive;

namespace Decho.ViewModels;

public sealed class MessageComposerViewModel : ViewModelBase
{
    public event Action<string, string>? SendRequested;

    public event Func<string, Task<string?>>? CommandRequested;

    public event Action<string, string>? FileUploadRequested;

    private CommandHandler? _commandHandler;

    private readonly ObservableCollection<UserViewModel> _onlineUsers = [];
    private readonly ObservableCollection<string> _channelNames = [];

    public string Draft
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string ServerUrl { get; private set; } = string.Empty;

    public bool IsConnected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public bool HasCommandHandler => _commandHandler is not null;

    public AutocompleteController Autocomplete { get; }

    public MessageComposerViewModel()
    {
        AutocompleteProvider mentionProvider = new(
            trigger: '@',
            itemsSource: () => _onlineUsers.Select(u => u.Username),
            insertPrefix: "@");

        AutocompleteProvider channelProvider = new(
            trigger: '#',
            itemsSource: () => _channelNames,
            insertPrefix: "#");

        Autocomplete = new AutocompleteController([mentionProvider, channelProvider]);

        IObservable<bool> canSend = this.WhenAnyValue(x => x.Draft, draft => !string.IsNullOrWhiteSpace(draft));
        SendCommand = ReactiveCommand.Create(Send, canSend);

        _ = this.WhenAnyValue(x => x.Draft).Subscribe(OnDraftChanged);
    }

    public void UpdateAvailableUsers(IEnumerable<UserViewModel> users)
    {
        _onlineUsers.Clear();
        foreach (UserViewModel user in users)
        {
            _onlineUsers.Add(user);
        }
    }

    public void UpdateAvailableChannels(IEnumerable<string> channelNames)
    {
        _channelNames.Clear();
        foreach (string name in channelNames)
        {
            _channelNames.Add(name);
        }
    }

    public void RequestFileUpload(string filePath)
    {
        if (!string.IsNullOrEmpty(ServerUrl))
        {
            FileUploadRequested?.Invoke(ServerUrl, filePath);
        }
    }

    public void SetServer(string serverUrl, bool isConnected = true)
    {
        ServerUrl = serverUrl;
        IsConnected = isConnected;
    }

    public void SetCommandHandler(CommandHandler handler)
    {
        _commandHandler = handler;
        this.RaisePropertyChanged(nameof(HasCommandHandler));
    }

    public bool IsCommand(string input)
    {
        return _commandHandler?.IsCommand(input) ?? input.StartsWith('/');
    }

    public void InsertAutocomplete(string item)
    {
        string? insertion = Autocomplete.GetInsertion(item);
        if (insertion is null)
        {
            return;
        }

        string before = Draft[..Autocomplete.TriggerCharIndex];
        Draft = $"{before}{insertion}";
        Autocomplete.Reset();
    }

    private void OnDraftChanged(string? draft)
    {
        Autocomplete.Update(draft ?? string.Empty);
    }

    private void Send()
    {
        string text = Draft.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Draft = string.Empty;

        if (_commandHandler is not null && _commandHandler.IsCommand(text))
        {
            _ = (CommandRequested?.Invoke(text));
        }
        else
        {
            SendRequested?.Invoke(ServerUrl, text);
        }
    }
}