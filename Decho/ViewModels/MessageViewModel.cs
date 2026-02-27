using Decho.Models;

using System;

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

    public string TimeText
    {
        get
        {
            // If today, show time only; otherwise, show date and time.
            var now = DateTimeOffset.Now;

            if (Model.SentAt.Date == now.Date)
            {
                return Model.SentAt.ToLocalTime().ToString("t");
            }

            return now.ToString("g");
        }
    }
}
