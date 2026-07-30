using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HdRezka;
using Rezui.Models;
using Rezui.Services;

namespace Rezui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly RezkaClientService _rezka;
    private readonly ImageCacheService _images;
    private CancellationTokenSource? _operationCancellation;
    private AppSettings _settings = new();
    private Media? _media;
    private MediaStream? _resolvedStream;

    public MainWindowViewModel(
        SettingsService settingsService,
        RezkaClientService rezka,
        ImageCacheService images,
        PlayerViewModel player)
    {
        _settingsService = settingsService;
        _rezka = rezka;
        _images = images;
        Player = player;

        QuickSearches =
        [
            new QuickSearchItem("Новинки", "новинки"),
            new QuickSearchItem("Фантастика", "фантастика"),
            new QuickSearchItem("Детективы", "детектив"),
            new QuickSearchItem("Аниме", "аниме")
        ];

        _ = InitializeAsync();
    }

    public PlayerViewModel Player { get; }

    public IReadOnlyList<QuickSearchItem> QuickSearches { get; }

    public ObservableCollection<MediaCardItem> Results { get; } = [];

    public ObservableCollection<MediaCardItem> Recent { get; } = [];

    public ObservableCollection<TranslationItem> Translations { get; } = [];

    public ObservableCollection<ChoiceItem> Seasons { get; } = [];

    public ObservableCollection<ChoiceItem> Episodes { get; } = [];

    public ObservableCollection<QualityItem> Qualities { get; } = [];

    public ObservableCollection<SubtitleItem> Subtitles { get; } = [];

    [ObservableProperty]
    private bool _isHomeVisible = true;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private bool _isDetailsVisible;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isLoginOpen;

    [ObservableProperty]
    private bool _isPlaybackOptionsOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isPremium;

    [ObservableProperty]
    private string _accountLabel = "Войти";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _origin = string.Empty;

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberSession = true;

    [ObservableProperty]
    private string _detailsTitle = string.Empty;

    [ObservableProperty]
    private string _detailsOriginalTitle = string.Empty;

    [ObservableProperty]
    private string _detailsDescription = string.Empty;

    [ObservableProperty]
    private string _detailsMeta = string.Empty;

    [ObservableProperty]
    private string _detailsRating = string.Empty;

    [ObservableProperty]
    private Task<Bitmap?> _detailsImageSource = Task.FromResult<Bitmap?>(null);

    private Uri? _detailsImageUrl;

    [ObservableProperty]
    private bool _isSeries;

    [ObservableProperty]
    private TranslationItem? _selectedTranslation;

    [ObservableProperty]
    private ChoiceItem? _selectedSeason;

    [ObservableProperty]
    private ChoiceItem? _selectedEpisode;

    [ObservableProperty]
    private QualityItem? _selectedQuality;

    [ObservableProperty]
    private SubtitleItem? _selectedSubtitle;

    partial void OnSelectedSeasonChanged(ChoiceItem? value)
    {
        Episodes.Clear();
        if (value is null || _media is null)
        {
            SelectedEpisode = null;
            return;
        }

        _ = LoadEpisodesForSeasonAsync(value.Value);
    }

    [RelayCommand]
    private void ShowHome() => Navigate(Page.Home);

    [RelayCommand]
    private void ShowSearch()
    {
        Navigate(Page.Search);
        StatusMessage = Results.Count == 0
            ? "Введите название фильма, сериала или аниме"
            : StatusMessage;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Origin = _settings.Origin;
        StatusMessage = string.Empty;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void OpenLogin()
    {
        if (!_rezka.IsConfigured)
        {
            IsSettingsOpen = true;
            StatusMessage = "Сначала укажите адрес зеркала";
            return;
        }

        StatusMessage = string.Empty;
        IsLoginOpen = true;
    }

    [RelayCommand]
    private void CloseLogin()
    {
        Password = string.Empty;
        IsLoginOpen = false;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var previousOrigin = _settings.Origin;
            await _rezka.ConfigureOriginAsync(Origin, cancellationToken);
            _settings = _rezka.Settings;
            Origin = _settings.Origin;
            if (!string.Equals(
                    previousOrigin,
                    Origin,
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplyAuthentication(null);
            }

            IsSettingsOpen = false;
            StatusMessage = "Зеркало подключено";
        });
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "Введите логин и пароль";
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            var state = await _rezka.LoginAsync(
                Login,
                Password,
                RememberSession,
                cancellationToken);
            ApplyAuthentication(state);
            Password = string.Empty;
            IsLoginOpen = false;
            StatusMessage = "Вход выполнен";
        });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await RunBusyAsync(async cancellationToken =>
        {
            await _rezka.LogoutAsync(cancellationToken);
            ApplyAuthentication(null);
            StatusMessage = "Вы вышли из аккаунта";
        });
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ShowSearch();
            return;
        }

        Navigate(Page.Search);
        await RunBusyAsync(async cancellationToken =>
        {
            StatusMessage = $"Ищем «{SearchQuery.Trim()}»…";
            var results = await _rezka.SearchAsync(SearchQuery, cancellationToken);
            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(CreateCard(
                    result.Title,
                    result.Url,
                    result.ImageUrl,
                    RezkaClientService.LocalizeCategory(result.Category)));
            }

            StatusMessage = Results.Count == 0
                ? "Ничего не найдено"
                : $"Найдено: {Results.Count}";
        });
    }

    [RelayCommand]
    private async Task QuickSearchAsync(string query)
    {
        SearchQuery = query;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ResolvePlaybackAsync()
    {
        if (_media is null)
        {
            return;
        }

        if (IsSeries && (SelectedSeason is null || SelectedEpisode is null))
        {
            StatusMessage = "Выберите сезон и серию";
            return;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            _resolvedStream = await _media.GetStreamAsync(
                SelectedSeason?.Value,
                SelectedEpisode?.Value,
                SelectedTranslation?.Name,
                cancellationToken: cancellationToken);

            Qualities.Clear();
            foreach (var quality in _resolvedStream.Qualities.Values.Reverse())
            {
                Qualities.Add(new QualityItem(
                    quality.Name,
                    quality.RequiresPremium,
                    quality.IsAvailable,
                    quality.Urls));
            }

            SelectedQuality = Qualities.FirstOrDefault(
                                  item =>
                                      item.IsAvailable &&
                                      string.Equals(
                                          item.Name,
                                          _resolvedStream.DefaultQuality,
                                          StringComparison.OrdinalIgnoreCase))
                              ?? Qualities.FirstOrDefault(item => item.IsAvailable);

            Subtitles.Clear();
            Subtitles.Add(new SubtitleItem("Без субтитров", null));
            foreach (var subtitle in _resolvedStream.Subtitles.Items.Values)
            {
                Subtitles.Add(new SubtitleItem(subtitle.Title, subtitle.Url));
            }

            SelectedSubtitle = Subtitles.FirstOrDefault(
                                   item =>
                                       string.Equals(
                                           item.Title,
                                           _resolvedStream.DefaultSubtitle,
                                           StringComparison.OrdinalIgnoreCase))
                               ?? Subtitles[0];
            IsPlaybackOptionsOpen = true;
        });
    }

    [RelayCommand]
    private void ClosePlaybackOptions() => IsPlaybackOptionsOpen = false;

    [RelayCommand]
    private async Task StartPlaybackAsync()
    {
        if (_media is null ||
            SelectedQuality is not { IsAvailable: true, Urls.Count: > 0 } quality)
        {
            StatusMessage = "Выберите доступное качество";
            return;
        }

        try
        {
            IsBusy = true;
            await Player.PlayAsync(
                _media.Name,
                quality.Urls[0],
                SelectedSubtitle?.Url,
                _media.Url);
            IsPlaybackOptionsOpen = false;
            Navigate(Page.Player);
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = ToUserMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BackFromPlayer()
    {
        Player.StopCommand.Execute(null);
        Navigate(Page.Details);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            _settings = await _settingsService.LoadAsync();
            Origin = _settings.Origin;
            RememberSession = _settings.RememberSession;
            RebuildRecent();

            if (string.IsNullOrWhiteSpace(_settings.Origin))
            {
                IsSettingsOpen = true;
                StatusMessage = "Укажите доступное вам зеркало HDRezka";
                await _rezka.InitializeAsync(_settings);
                return;
            }

            var state = await _rezka.InitializeAsync(_settings);
            ApplyAuthentication(state);
            StatusMessage = state?.IsAuthenticated == true
                ? "Сессия восстановлена"
                : "Готово к поиску";
        }
        catch (Exception exception)
        {
            StatusMessage = ToUserMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenMediaAsync(Uri url)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            StatusMessage = "Загружаем карточку…";
            _media = await _rezka.LoadMediaAsync(url, cancellationToken);
            DetailsTitle = _media.Name;
            DetailsOriginalTitle = _media.OriginalName ?? string.Empty;
            DetailsDescription = _media.Description;
            _detailsImageUrl = _media.ThumbnailHighQuality ?? _media.Thumbnail;
            DetailsImageSource = _images.LoadAsync(_detailsImageUrl, _media.Url);
            DetailsMeta = string.Join(
                "  ·  ",
                new[]
                {
                    _media.ReleaseYear?.ToString(),
                    RezkaClientService.LocalizeCategory(_media.Category),
                    _media.Format == MediaFormat.Series ? "Сериал" : "Фильм"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            DetailsRating = _media.Rating.Value is { } rating
                ? $"★ {rating:0.0}" +
                  (_media.Rating.Votes is { } votes ? $"  ·  {votes:N0} оценок" : string.Empty)
                : "Рейтинг не указан";
            IsSeries = _media.Format == MediaFormat.Series;

            Translations.Clear();
            foreach (var translator in _media.TranslationOptions)
            {
                Translations.Add(new TranslationItem(
                    translator.Id,
                    translator.Name,
                    translator.IsPremium));
            }

            SelectedTranslation =
                Translations.FirstOrDefault(item => !item.IsPremium || _media.IsPremiumAccount)
                ?? Translations.FirstOrDefault();

            Seasons.Clear();
            Episodes.Clear();
            if (IsSeries)
            {
                var seasons = await _media.GetEpisodesInfoAsync(cancellationToken);
                foreach (var season in seasons)
                {
                    Seasons.Add(new ChoiceItem(season.Number, season.Title));
                }

                SelectedSeason = Seasons.FirstOrDefault();
            }

            await _rezka.SaveRecentAsync(
                _media.Name,
                _media.Url,
                _detailsImageUrl,
                _media.Category,
                cancellationToken);
            RebuildRecent();
            Navigate(Page.Details);
            StatusMessage = string.Empty;
        });
    }

    private async Task LoadEpisodesForSeasonAsync(int seasonNumber)
    {
        if (_media is null)
        {
            return;
        }

        try
        {
            var seasons = await _media.GetEpisodesInfoAsync();
            var season = seasons.FirstOrDefault(item => item.Number == seasonNumber);
            Episodes.Clear();
            if (season is not null)
            {
                foreach (var episode in season.Episodes)
                {
                    Episodes.Add(new ChoiceItem(episode.Number, episode.Title));
                }
            }

            SelectedEpisode = Episodes.FirstOrDefault();
        }
        catch (Exception exception)
        {
            StatusMessage = ToUserMessage(exception);
        }
    }

    private MediaCardItem CreateCard(
        string title,
        Uri url,
        Uri? imageUrl,
        string category) =>
        new(
            title,
            url,
            _images.LoadAsync(imageUrl, url),
            category,
            () => OpenMediaAsync(url));

    private void RebuildRecent()
    {
        Recent.Clear();
        foreach (var item in _settings.Recent.Take(12))
        {
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var url))
            {
                continue;
            }

            Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var image);
            Recent.Add(CreateCard(item.Title, url, image, item.Category));
        }
    }

    private void ApplyAuthentication(AuthenticationState? state)
    {
        IsAuthenticated = state?.IsAuthenticated == true;
        IsPremium = state?.IsPremium == true;
        AccountLabel = IsAuthenticated
            ? IsPremium ? "Premium" : "Аккаунт"
            : "Войти";
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await action(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Операция отменена";
        }
        catch (Exception exception)
        {
            StatusMessage = ToUserMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Navigate(Page page)
    {
        IsHomeVisible = page == Page.Home;
        IsSearchVisible = page == Page.Search;
        IsDetailsVisible = page == Page.Details;
        IsPlayerVisible = page == Page.Player;
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        LoginFailedException => "Не удалось войти: проверьте логин и пароль",
        LoginRequiredException => "Для этого действия нужно войти в аккаунт",
        PremiumRequiredException => "Выбранный перевод или качество требует Premium",
        CaptchaException => "Сайт запросил CAPTCHA. Откройте зеркало в браузере и повторите попытку",
        HttpException => "Зеркало вернуло ошибку. Проверьте адрес или попробуйте позже",
        ParseException => "Структура страницы изменилась, данные не удалось разобрать",
        HttpRequestException => "Нет связи с зеркалом. Проверьте интернет и адрес",
        UriFormatException => "Некорректный адрес зеркала",
        ArgumentException argument => argument.Message,
        DllNotFoundException => "LibVLC не найден. Установите VLC/libvlc для вашей системы",
        PlayerRuntimeException runtime => runtime.Message,
        LibVLCSharp.Shared.VLCException vlc => $"Не удалось запустить LibVLC: {vlc.Message}",
        _ => $"Ошибка: {exception.Message}"
    };

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
    }

    private enum Page
    {
        Home,
        Search,
        Details,
        Player
    }
}

public sealed record QuickSearchItem(string Title, string Query);
