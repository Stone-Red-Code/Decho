using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using EchoHub.Core.DTOs;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Decho.Services;

internal sealed class AvatarService : IAvatarService
{
    // The server does not announce avatar changes, so entries are re-fetched after this long.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(1);

    private readonly IConnectionStore _store;
    private readonly Dictionary<(string ServerUrl, string Username), Entry> _entries = [];

    // One request at a time: ApiClient refreshes expired tokens without locking, so
    // parallel requests would all try to redeem the same refresh token.
    private readonly SemaphoreSlim _requestLimiter = new SemaphoreSlim(1, 1);

    public event Action<string, string>? AvatarChanged;

    public AvatarService(IConnectionStore store)
    {
        _store = store;
    }

    public Task<Bitmap?> GetAvatarAsync(string serverUrl, string username)
    {
        lock (_entries)
        {
            Entry entry = GetOrCreateEntry(serverUrl, username);

            if (entry.Pending is not null)
            {
                return entry.Pending;
            }

            if (DateTimeOffset.UtcNow < entry.ExpiresAt)
            {
                return Task.FromResult(entry.Bitmap);
            }

            entry.Pending = Task.Run(() => FetchAsync(serverUrl, username, entry));
            return entry.Pending;
        }
    }

    public void Publish(string serverUrl, string username, string? avatarAscii)
    {
        Entry entry;
        lock (_entries)
        {
            entry = GetOrCreateEntry(serverUrl, username);
            entry.ExpiresAt = DateTimeOffset.UtcNow + CacheLifetime;

            if (entry.Ascii == avatarAscii)
            {
                return;
            }
        }

        Bitmap? bitmap = CreateBitmap(avatarAscii);

        lock (_entries)
        {
            entry.Ascii = avatarAscii;
            entry.Bitmap = bitmap;
        }

        AvatarChanged?.Invoke(serverUrl, username);
    }

    private Entry GetOrCreateEntry(string serverUrl, string username)
    {
        (string, string) key = (serverUrl.ToLowerInvariant(), username.ToLowerInvariant());
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry();
            _entries[key] = entry;
        }

        return entry;
    }

    private async Task<Bitmap?> FetchAsync(string serverUrl, string username, Entry entry)
    {
        await _requestLimiter.WaitAsync();
        try
        {
            ServerConnection? connection = _store.Get(serverUrl);
            if (connection is null)
            {
                throw new InvalidOperationException("Not connected");
            }

            UserProfileDto? profile = await connection.ApiClient.GetUserProfileAsync(username);
            Publish(serverUrl, username, profile?.AvatarAscii);
        }
        catch (Exception ex)
        {
            // Keep whatever picture we already have and try again later.
            Debug.WriteLine($"Failed to load avatar for {username}: {ex.Message}");
            lock (_entries)
            {
                entry.ExpiresAt = DateTimeOffset.UtcNow + FailureRetryDelay;
            }
        }
        finally
        {
            _ = _requestLimiter.Release();
            lock (_entries)
            {
                entry.Pending = null;
            }
        }

        lock (_entries)
        {
            return entry.Bitmap;
        }
    }

    private static Bitmap? CreateBitmap(string? avatarAscii)
    {
        DecodedAvatar? decoded = AvatarAsciiDecoder.Decode(avatarAscii);
        if (decoded is null)
        {
            return null;
        }

        WriteableBitmap bitmap = new WriteableBitmap(
            new PixelSize(decoded.Width, decoded.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using ILockedFramebuffer framebuffer = bitmap.Lock();
        int sourceStride = decoded.Width * 4;
        for (int y = 0; y < decoded.Height; y++)
        {
            Marshal.Copy(decoded.Bgra, y * sourceStride, framebuffer.Address + (y * framebuffer.RowBytes), sourceStride);
        }

        return bitmap;
    }

    private sealed class Entry
    {
        public string? Ascii { get; set; }

        public Bitmap? Bitmap { get; set; }

        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.MinValue;

        public Task<Bitmap?>? Pending { get; set; }
    }
}
