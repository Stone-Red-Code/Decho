namespace Decho.Services;

internal interface IConnectionStore
{
    ServerConnection? Get(string serverUrl);
}
