using System;
using System.Reactive;
using System.Reactive.Linq;

using ReactiveUI;

namespace Decho.ViewModels;

public sealed class MessageComposerViewModel : ViewModelBase
{
    private string _draft = string.Empty;

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

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public event Action<string>? SendRequested;

    private void Send()
    {
        var text = Draft.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        Draft = string.Empty;
        SendRequested?.Invoke(text);
    }
}
