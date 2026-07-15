using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

using EchoHub.Client.Commands;
using ReactiveUI;

namespace Decho.ViewModels;

public sealed class MessageComposerViewModel : ViewModelBase
{
    private string _draft = string.Empty;
    private CommandHandler? _commandHandler;
    private string _serverUrl = string.Empty;

    public MessageComposerViewModel()
    {
        var canSend = this.WhenAnyValue(x => x.Draft, draft => !string.IsNullOrWhiteSpace(draft));
        SendCommand = ReactiveCommand.Create(Send, canSend);
    }

    public string Draft
    {
        get => _draft;
        set => this.RaiseAndSetIfChanged(ref _draft, value);
    }

    public string ServerUrl => _serverUrl;

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public bool HasCommandHandler => _commandHandler is not null;

    public event Action<string, string>? SendRequested;
    public event Func<string, Task<string?>>? CommandRequested;
    public event Action<string, string>? FileUploadRequested;

    public void RequestFileUpload(string filePath)
    {
        if (!string.IsNullOrEmpty(_serverUrl))
            FileUploadRequested?.Invoke(_serverUrl, filePath);
    }

    public void SetServer(string serverUrl)
    {
        _serverUrl = serverUrl;
    }

    public void SetCommandHandler(CommandHandler handler)
    {
        _commandHandler = handler;
        this.RaisePropertyChanged(nameof(HasCommandHandler));
    }

    public bool IsCommand(string input) => _commandHandler?.IsCommand(input) ?? input.StartsWith('/');

    private void Send()
    {
        var text = Draft.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        Draft = string.Empty;

        if (_commandHandler is not null && _commandHandler.IsCommand(text))
        {
            CommandRequested?.Invoke(text);
        }
        else
        {
            SendRequested?.Invoke(_serverUrl, text);
        }
    }
}