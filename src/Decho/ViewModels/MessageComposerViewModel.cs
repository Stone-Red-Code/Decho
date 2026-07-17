using Decho.Models;

using EchoHub.Client.Commands;
using EchoHub.Core.Constants;

using System.Collections.ObjectModel;
using System.Reactive;

namespace Decho.ViewModels;

public sealed class MessageComposerViewModel : ViewModelBase
{
    public event Action<string, string, IReadOnlyList<string>>? SendRequested;

    public event Func<string, Task<string?>>? CommandRequested;

    private readonly ObservableCollection<UserViewModel> _onlineUsers = [];
    private readonly ObservableCollection<string> _channelNames = [];
    private CommandHandler? _commandHandler;

    public ObservableCollection<StagedFile> StagedFiles { get; } = [];

    public bool HasStagedFiles => StagedFiles.Count > 0;

    public string StagedFilesSummary
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

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

        IObservable<bool> canSend = this.WhenAnyValue(
            x => x.Draft,
            x => x.HasStagedFiles,
            (draft, hasFiles) => !string.IsNullOrWhiteSpace(draft) || hasFiles);
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

    public void StageFiles(IEnumerable<string> filePaths)
    {
        int remaining = HubConstants.MaxAttachmentsPerMessage - StagedFiles.Count;
        foreach (string path in filePaths)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (StagedFiles.Any(f => f.FilePath == path))
            {
                continue;
            }

            StagedFiles.Add(new StagedFile(path, Path.GetFileName(path)));
            remaining--;
        }

        UpdateStagedSummary();
    }

    public void RemoveStagedFile(StagedFile file)
    {
        _ = StagedFiles.Remove(file);
        UpdateStagedSummary();
    }

    public void ClearStagedFiles()
    {
        StagedFiles.Clear();
        UpdateStagedSummary();
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

    private void UpdateStagedSummary()
    {
        this.RaisePropertyChanged(nameof(HasStagedFiles));
        StagedFilesSummary = StagedFiles.Count > 0
            ? $"{StagedFiles.Count} file(s) staged"
            : string.Empty;
    }

    private void OnDraftChanged(string? draft)
    {
        Autocomplete.Update(draft ?? string.Empty);
    }

    private void Send()
    {
        string text = Draft.Trim();
        Draft = string.Empty;

        if (_commandHandler is not null && _commandHandler.IsCommand(text))
        {
            _ = (CommandRequested?.Invoke(text));
            return;
        }

        List<string> filePaths = StagedFiles.Select(f => f.FilePath).ToList();
        StagedFiles.Clear();
        UpdateStagedSummary();

        SendRequested?.Invoke(ServerUrl, text, filePaths);
    }
}