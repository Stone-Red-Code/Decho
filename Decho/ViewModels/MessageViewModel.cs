using Decho.Models;

namespace Decho.ViewModels;

public sealed class MessageViewModel : ViewModelBase
{
    public MessageViewModel(MessageModel model)
    {
        Model = model;
    }

    public MessageModel Model { get; }

    public string AuthorName => Model.Author.DisplayName;

    public string Content => Model.Content;

    public string TimeText => Model.SentAt.ToLocalTime().ToString("h:mm tt");
}
