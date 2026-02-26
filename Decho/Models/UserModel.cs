namespace Decho.Models;

public sealed class UserModel
{
    public UserModel(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }
}
