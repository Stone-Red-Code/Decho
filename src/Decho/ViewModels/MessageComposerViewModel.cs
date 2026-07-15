using EchoHub.Client.Commands;

using System.Reactive;

namespace Decho.ViewModels;

public sealed class MessageComposerViewModel : ViewModelBase
{
    public event Action<string, string>? SendRequested;

    public event Func<string, Task<string?>>? CommandRequested;

    public event Action<string, string>? FileUploadRequested;

    private CommandHandler? _commandHandler;

    public string Draft
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string ServerUrl { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public bool HasCommandHandler => _commandHandler is not null;

    public MessageComposerViewModel()
    {
        IObservable<bool> canSend = this.WhenAnyValue(x => x.Draft, draft => !string.IsNullOrWhiteSpace(draft));
        SendCommand = ReactiveCommand.Create(Send, canSend);
    }

    public void RequestFileUpload(string filePath)
    {
        if (!string.IsNullOrEmpty(ServerUrl))
        {
            FileUploadRequested?.Invoke(ServerUrl, filePath);
        }
    }

    public void SetServer(string serverUrl)
    {
        ServerUrl = serverUrl;
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