using HdRezka;
using Rezui.Models;
using Serilog;

namespace Rezui.Services;

public interface ILibrarySnapshotProvider
{
    Task<AccountLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default);
}

public sealed class RezkaClientService : ILibrarySnapshotProvider, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly LocalCacheStore? _cache;
    private readonly ILogger _logger;
    private Client? _client;
    private Media? _currentMedia;
    private AppSettings? _settings;
    private AuthState _auth = new();

    public RezkaClientService(
        SettingsService settingsService,
        LocalCacheStore? cache = null,
        ILogger? logger = null)
    {
        _settingsService = settingsService;
        _cache = cache;
        _logger = logger ?? Log.ForContext<RezkaClientService>();
    }

    public bool IsConfigured => _client is not null;

    public Uri? Origin => _client?.Origin;

    public AppSettings Settings =>
        _settings ?? throw new InvalidOperationException("The service is not initialized.");

    public AuthState Auth => _auth;

    public Media? CurrentMedia => _currentMedia;

    internal void AttachSettings(AppSettings settings) => _settings = settings;

    internal void AttachAuth(AuthState auth) => _auth = auth;

    public async Task<AuthenticationState?> InitializeAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        AttachSettings(settings);
        _auth = await _settingsService.LoadAuthAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Origin))
        {
            return null;
        }

        CreateClient(settings.Origin, _auth.Cookies);
        _logger.Information(
            "HDRezka client initialized for {OriginHost}; remembered session={HasRememberedSession}",
            _client!.Origin?.Host ?? "unknown",
            settings.RememberSession && _auth.Cookies.Count > 0);
        if (!settings.RememberSession || _auth.Cookies.Count == 0)
        {
            return null;
        }

        var state = await _client!.GetAuthenticationStateAsync(cancellationToken);
        if (!state.IsAuthenticated)
        {
            _logger.Warning("Remembered HDRezka session is no longer authenticated");
            await ForgetSessionAsync(cancellationToken);
        }
        else
        {
            _logger.Information("Remembered HDRezka session restored");
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
            _auth.Cookies.Clear();
            await _settingsService.ClearAuthAsync(cancellationToken);
            if (_cache is not null)
            {
                await _cache.RemoveAreaAsync(CacheArea.Account, cancellationToken);
                await _cache.RemoveAreaAsync(CacheArea.Library, cancellationToken);
            }
        }

        CreateClient(_settings.Origin, _auth.Cookies);
        await _settingsService.SaveAsync(_settings, cancellationToken);
        _logger.Information(
            "HDRezka origin configured as {OriginHost}; changed={OriginChanged}",
            normalized.Host,
            changed);
    }

    public async Task<AuthenticationState> LoginAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var state = await _client!.LoginAsync(
            login.Trim(),
            password,
            rememberMe: true,
            cancellationToken);

        Settings.RememberSession = true;
        _auth.Cookies.Clear();
        CopyAuthenticationCookies(
            _client.Options.Cookies,
            state.CookieNames,
            _auth.Cookies);

        await _settingsService.SaveAsync(Settings, cancellationToken);
        await _settingsService.SaveAuthAsync(_auth, cancellationToken);
        if (_cache is not null)
        {
            await _cache.RemoveAreaAsync(CacheArea.Account, cancellationToken);
            await _cache.RemoveAreaAsync(CacheArea.Library, cancellationToken);
        }
        _logger.Information("HDRezka login completed successfully");
        return state;
    }

    public async Task<AccountProfile> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var cacheKey = GetAccountCacheKey("profile");
        try
        {
            var profile = await _client!.Account.GetProfileAsync(cancellationToken);
            if (_cache is not null)
            {
                await _cache.SetJsonAsync(
                    CacheArea.Account,
                    cacheKey,
                    profile,
                    TimeSpan.FromHours(6),
                    cancellationToken);
            }

            return profile;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not LoginRequiredException &&
            _cache is not null)
        {
            var cached = await _cache.GetJsonAsync<AccountProfile>(
                CacheArea.Account,
                cacheKey,
                cancellationToken);
            if (cached is not null)
            {
                _logger.Warning(
                    exception,
                    "Profile request failed; using cached profile");
                return cached;
            }

            throw;
        }
    }

    public async Task<AccountLibrarySnapshot> GetLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var cacheKey = GetAccountCacheKey("library");
        try
        {
            var continueWatchingTask =
                _client!.Account.GetContinueWatchingAsync(cancellationToken);
            var bookmarksTask =
                _client.Account.GetBookmarksAsync(cancellationToken);

            await Task.WhenAll(continueWatchingTask, bookmarksTask);
            var snapshot = new AccountLibrarySnapshot(
                await continueWatchingTask,
                await bookmarksTask);
            if (_cache is not null)
            {
                await _cache.SetJsonAsync(
                    CacheArea.Library,
                    cacheKey,
                    snapshot,
                    TimeSpan.FromMinutes(20),
                    cancellationToken);
            }

            return snapshot;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not LoginRequiredException &&
            _cache is not null)
        {
            var cached = await _cache.GetJsonAsync<AccountLibrarySnapshot>(
                CacheArea.Library,
                cacheKey,
                cancellationToken);
            if (cached is not null)
            {
                _logger.Warning(
                    exception,
                    "Library request failed; using cached library snapshot");
                return cached;
            }

            throw;
        }
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
            _logger.Information("HDRezka session logged out and local authentication cleared");
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
        CancellationToken cancellationToken = default,
        string? preferredTitle = null)
    {
        EnsureConfigured();
        var media = await _client!.GetAsync(url, cancellationToken);
        _logger.Information(
            "Loaded media metadata from {MediaHost}{MediaPath}",
            url.Host,
            url.AbsolutePath);
        var previous = Interlocked.Exchange(ref _currentMedia, media);
        previous?.Dispose();
        if (_cache is not null)
        {
            try
            {
                await _cache.SetJsonAsync(
                    CacheArea.MediaMetadata,
                    NormalizeMediaCacheKey(url),
                    CreateMetadataSnapshot(media, preferredTitle),
                    TimeSpan.FromDays(14),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException and not LoginRequiredException)
            {
                _logger.Warning(
                    exception,
                    "Could not persist media metadata cache for {MediaPath}",
                    url.AbsolutePath);
            }
        }

        return media;
    }

    public async Task<CachedMediaMetadata?> GetCachedMediaMetadataAsync(
        Uri url,
        CancellationToken cancellationToken = default)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var cached = await _cache.GetJsonAsync<CachedMediaMetadata>(
                CacheArea.MediaMetadata,
                NormalizeMediaCacheKey(url),
                cancellationToken);
            _logger.Debug(
                "Media metadata cache {CacheOutcome} for {MediaPath}",
                cached is null ? "miss" : "hit",
                url.AbsolutePath);
            return cached;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning(
                exception,
                "Media metadata cache read failed for {MediaPath}",
                url.AbsolutePath);
            return null;
        }
    }

    public async Task<CachedCommentPage?> GetCachedCommentsAsync(
        Media media,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            return await _cache.GetJsonAsync<CachedCommentPage>(
                CacheArea.Comments,
                GetCommentsCacheKey(media, page),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning(
                exception,
                "Comments cache read failed for {MediaPath}",
                media.Url.AbsolutePath);
            return null;
        }
    }

    public async Task<CachedCommentPage> GetCommentsAsync(
        Media media,
        int page,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCommentsCacheKey(media, page);
        try
        {
            var comments = await media.Comments.GetPageAsync(page, cancellationToken);
            var snapshot = new CachedCommentPage(
                comments.Items.Select(comment => new CachedComment(
                    comment.Id,
                    comment.ParentId,
                    comment.Depth,
                    comment.Author,
                    comment.AvatarUrl?.AbsoluteUri,
                    comment.DateLabel,
                    comment.Text,
                    comment.Likes,
                    comment.Url.AbsoluteUri)).ToArray(),
                comments.Page,
                comments.TotalPages,
                comments.LastUpdateId);
            if (_cache is not null)
            {
                await _cache.SetJsonAsync(
                    CacheArea.Comments,
                    cacheKey,
                    snapshot,
                    TimeSpan.FromHours(2),
                    cancellationToken);
            }

            return snapshot;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not LoginRequiredException &&
            _cache is not null)
        {
            var cached = await _cache.GetJsonAsync<CachedCommentPage>(
                CacheArea.Comments,
                cacheKey,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            throw;
        }
    }

    public async Task SaveRecentAsync(
        string title,
        Uri url,
        Uri? imageUrl,
        MediaCategory category,
        CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            await _cache.SaveRecentAsync(
                GetRecentScope(),
                new RecentMedia(
                title,
                url.AbsoluteUri,
                imageUrl?.AbsoluteUri ?? string.Empty,
                LocalizeCategory(category),
                DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    public Task<IReadOnlyList<RecentMedia>> GetRecentAsync(
        CancellationToken cancellationToken = default) =>
        _cache?.GetRecentAsync(GetRecentScope(), 20, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<RecentMedia>>([]);

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
            throw new ArgumentException("Укажите корректный HTTP(S)-адрес зеркала", nameof(origin));
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
        var options = new ClientOptions
        {
            ResponseCacheDuration = TimeSpan.FromSeconds(30),
            MaxCachedResponses = 128
        };
        foreach (var pair in cookies)
        {
            options.Cookies[pair.Key] = pair.Value;
        }

        var next = new Client(origin, options);
        var previous = Interlocked.Exchange(ref _client, next);
        previous?.Dispose();

        var oldMedia = Interlocked.Exchange(ref _currentMedia, null);
        oldMedia?.Dispose();
        _logger.Debug(
            "Created HDRezka client for {OriginHost} with {ResponseCacheSeconds}s response cache",
            next.Origin?.Host ?? "unknown",
            options.ResponseCacheDuration.TotalSeconds);
    }

    private async Task ForgetSessionAsync(CancellationToken cancellationToken)
    {
        _auth.Cookies.Clear();
        await _settingsService.SaveAsync(Settings, cancellationToken);
        await _settingsService.ClearAuthAsync(cancellationToken);
        if (_cache is not null)
        {
            await _cache.RemoveAreaAsync(CacheArea.Account, cancellationToken);
            await _cache.RemoveAreaAsync(CacheArea.Library, cancellationToken);
        }
    }

    private string GetAccountCacheKey(string suffix) =>
        $"{GetRecentScope()}|{suffix}";

    private string GetRecentScope() =>
        BuildAccountScope(
            Origin?.GetLeftPart(UriPartial.Authority) ?? string.Empty,
            _auth.Cookies);

    /// <summary>
    /// Builds the per-account cache scope so user-bound data (recent history,
    /// library) never leaks between accounts on the same machine, while the
    /// generic areas (media metadata, comments) stay shared.
    /// </summary>
    public static string BuildAccountScope(
        string originAuthority,
        IReadOnlyDictionary<string, string> cookies)
    {
        cookies.TryGetValue("dle_user_id", out var userId);
        return $"{originAuthority}|{userId ?? "unknown"}";
    }

    private static string NormalizeMediaCacheKey(Uri url) =>
        url.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();

    private static string GetCommentsCacheKey(Media media, int page) =>
        $"{NormalizeMediaCacheKey(media.Url)}|page:{page}";

    internal static CachedMediaMetadata CreateMetadataSnapshot(
        Media media,
        string? preferredTitle = null)
    {
        var details = media.Details;
        return new CachedMediaMetadata(
            media.Url.AbsoluteUri,
            media.Id,
            TitleFormatter.Reconcile(media.Name, preferredTitle),
            media.Names.ToArray(),
            media.OriginalNames.ToArray(),
            media.Description,
            (media.ThumbnailHighQuality ?? media.Thumbnail)?.AbsoluteUri,
            media.ReleaseYear,
            media.Format.ToString(),
            LocalizeCategory(media.Category),
            media.Playback.IsAvailable,
            media.Rating.Value,
            media.Rating.Votes,
            details.Tagline,
            details.ReleaseDate,
            details.Countries.Select(ToCachedLink).ToArray(),
            details.Genres.Select(ToCachedLink).ToArray(),
            details.Directors.Select(ToCachedPerson).ToArray(),
            details.Cast.Select(ToCachedPerson).ToArray(),
            details.Quality,
            details.AgeRating,
            details.Duration is { } duration ? (long)duration.TotalSeconds : null,
            details.Collections.Select(ToCachedLink).ToArray(),
            details.Rankings.Select(ToCachedLink).ToArray(),
            details.ExternalRatings.Select(rating => new CachedExternalRating(
                rating.Source,
                rating.Value,
                rating.Votes,
                rating.Url?.AbsoluteUri)).ToArray(),
            details.Recommendations.Select(item => new CachedMediaPreview(
                item.Title,
                item.Url.AbsoluteUri,
                item.ImageUrl?.AbsoluteUri,
                LocalizeCategory(item.Category),
                item.Details,
                item.Information)).ToArray(),
            details.Schedule.Select(item => new CachedScheduleEntry(
                item.Id,
                item.Season,
                item.Episode,
                item.Title,
                item.OriginalTitle,
                item.ReleaseDate,
                item.IsAvailable,
                item.IsWatched)).ToArray(),
            media.TranslationOptions.Select(item => new CachedTranslator(
                item.Id,
                item.Name,
                item.IsPremium,
                item.IsCamrip,
                item.HasAds,
                item.IsDirectorCut)).ToArray(),
            media.OtherParts.Select(item => new CachedNamedLink(
                item.Title,
                item.Url.AbsoluteUri)).ToArray());
    }

    private static CachedNamedLink ToCachedLink(NamedLink item) =>
        new(item.Name, item.Url.AbsoluteUri);

    private static CachedPerson ToCachedPerson(PersonInfo item) =>
        new(
            item.Id,
            item.Name,
            item.Job,
            item.Url.AbsoluteUri,
            item.ImageUrl?.AbsoluteUri);

    private static void CopyAuthenticationCookies(
        IDictionary<string, string> source,
        IEnumerable<string> authenticationCookieNames,
        IDictionary<string, string> destination)
    {
        var names = authenticationCookieNames
            .Concat(new[] { "dle_user_id", "dle_password", "dle_hash" })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
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
            throw new InvalidOperationException("Сначала укажите адрес доступного зеркала");
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
