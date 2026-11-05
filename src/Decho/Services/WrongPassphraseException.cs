namespace Decho.Services;

/// <summary>The passphrase given for an end-to-end encrypted channel didn't unlock its room key.</summary>
public sealed class WrongPassphraseException : InvalidOperationException
{
    public WrongPassphraseException()
        : base("Wrong passphrase")
    {
    }
}
