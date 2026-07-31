using HdRezka;
using Rezui.Models;

namespace Rezui.Services;

public interface ILibrarySnapshotProvider
{
    Task<AccountLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default);
}

public sealed class RezkaClientService : ILibrarySnapshotProvider, IDisposable
{
    private readonly SettingsService _settingsService;
    private Client? _client;
    private Media? _currentMedia;
    private AppSettings? _settings;

    public RezkaClientService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsConfigured => _client is not null;

    public Uri? Origin => _client?.Origin;

    public AppSettings Settings =>
        _settings ?? throw new InvalidOperationException("The service is not initialized.");

    public Media? CurrentMedia => _currentMedia;

    internal void AttachSettings(AppSettings settings) => _settings = settings;

    public async Task<AuthenticationState?> InitializeAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        AttachSettings(settings);
        if (string.IsNullOrWhiteSpace(settings.Origin))
        {
            return null;
        }

        CreateClient(settings.Origin, settings.AuthenticationCookies);
        if (settings.AuthenticationCookies.Count == 0)
        {
            return null;
        }

        var state = await _client!.GetAuthenticationStateAsync(cancellationToken);
        if (!state.IsAuthenticated)
        {
            await ForgetSessionAsync(cancellationToken);
        }

        return state;
    }

    public async Task ConfigureOriginAsync(
        string origin,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeOrigin(origin);
        if (_settings is null)
        {
            throw new InvalidOperationException("The service is not initialized.");
        }

        var changed = !string.Equals(
            _settings.Origin,
            normalized.AbsoluteUri.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

        _settings.Origin = normalized.AbsoluteUri.TrimEnd('/');
        if (changed)
        {
            _settings.AuthenticationCookies.Clear();
        }

        CreateClient(_settings.Origin, _settings.AuthenticationCookies);
        await _settingsService.SaveAsync(_settings, cancellationToken);
    }

    public async Task<AuthenticationState> LoginAsync(
        string login,
        string password,
        bool rememberSession,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var state = await _client!.LoginAsync(
            login.Trim(),
            password,
            rememberMe: rememberSession,
            cancellationToken);

        Settings.RememberSession = rememberSession;
        Settings.AuthenticationCookies.Clear();
        if (rememberSession)
        {
            CopyAuthenticationCookies(_client.Options.Cookies, Settings.AuthenticationCookies);
        }

        await _settingsService.SaveAsync(Settings, cancellationToken);
        return state;
    }

    public async Task<AccountProfile> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _client!.Account.GetProfileAsync(cancellationToken);
    }

    public async Task<AccountLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var continueWatchingTask =
            _client!.Account.GetContinueWatchingAsync(cancellationToken);
        var bookmarksTask =
            _client.Account.GetBookmarksAsync(cancellationToken);

        await Task.WhenAll(continueWatchingTask, bookmarksTask);
        return new AccountLibrarySnapshot(
            await continueWatchingTask,
            await bookmarksTask);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        try
        {
            await _client!.LogoutAsync(cancellationToken);
        }
        finally
        {
            await ForgetSessionAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _client!.SearchPageAsync(query.Trim(), 1, cancellationToken);
    }

    public async Task<Media> LoadMediaAsync(
        Uri url,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var media = await _client!.GetAsync(url, cancellationToken);
        var previous = Interlocked.Exchange(ref _currentMedia, media);
        previous?.Dispose();
        return media;
    }

    public async Task SaveRecentAsync(
        string title,
        Uri url,
        Uri? imageUrl,
        MediaCategory category,
        CancellationToken cancellationToken = default)
    {
        var recent = Settings.Recent;
        recent.RemoveAll(item =>
            string.Equals(item.Url, url.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        recent.Insert(
            0,
            new RecentMedia(
                title,
                url.AbsoluteUri,
                imageUrl?.AbsoluteUri ?? string.Empty,
                LocalizeCategory(category),
                DateTimeOffset.UtcNow));
        if (recent.Count > 20)
        {
            recent.RemoveRange(20, recent.Count - 20);
        }

        await _settingsService.SaveAsync(Settings, cancellationToken);
    }

    public static Uri NormalizeOrigin(string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var candidate = origin.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Укажите корректный HTTP(S)-адрес зеркала.", nameof(origin));
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    public static string LocalizeCategory(MediaCategory category) => category switch
    {
        MediaCategory.Film => "Фильм",
        MediaCategory.Series => "Сериал",
        MediaCategory.Cartoon => "Мультфильм",
        MediaCategory.Anime => "Аниме",
        _ => "Видео"
    };

    private void CreateClient(
        string origin,
        IReadOnlyDictionary<string, string> cookies)
    {
        var options = new ClientOptions();
        foreach (var pair in cookies)
        {
            options.Cookies[pair.Key] = pair.Value;
        }

        var next = new Client(origin, options);
        var previous = Interlocked.Exchange(ref _client, next);
        previous?.Dispose();

        var oldMedia = Interlocked.Exchange(ref _currentMedia, null);
        oldMedia?.Dispose();
    }

    private async Task ForgetSessionAsync(CancellationToken cancellationToken)
    {
        Settings.AuthenticationCookies.Clear();
        await _settingsService.SaveAsync(Settings, cancellationToken);
    }

    private static void CopyAuthenticationCookies(
        IDictionary<string, string> source,
        IDictionary<string, string> destination)
    {
        foreach (var name in new[] { "dle_user_id", "dle_password", "dle_hash" })
        {
            if (source.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                destination[name] = value;
            }
        }
    }

    private void EnsureConfigured()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Сначала укажите адрес доступного зеркала.");
        }
    }

    public void Dispose()
    {
        _currentMedia?.Dispose();
        _client?.Dispose();
    }
}

public sealed record AccountLibrarySnapshot(
    IReadOnlyList<ContinueWatchingEntry> ContinueWatching,
    IReadOnlyList<BookmarkFolder> BookmarkFolders);
