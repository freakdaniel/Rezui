using System.Collections.ObjectModel;
using System.Net.Mail;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HdRezka;
using Rezui.Models;
using Rezui.Services;

namespace Rezui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string LoginPrompt =
        "Войдите в приложение используя свой персональный аккаунт HDRezka";

    private readonly SettingsService _settingsService;
    private readonly RezkaClientService _rezka;
    private readonly ImageCacheService _images;
    private readonly ThemeService _themes;
    private readonly LibrarySyncWorker _librarySync;
    private readonly IMirrorDiscoveryService _mirrorDiscovery;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _loginStatusCancellation;
    private CancellationTokenSource? _mirrorStatusCancellation;
    private AppSettings _settings = new();
    private Media? _media;
    private MediaStream? _resolvedStream;
    private volatile bool _disposed;

    public MainWindowViewModel(
        SettingsService settingsService,
        RezkaClientService rezka,
        ImageCacheService images,
        PlayerViewModel player,
        ThemeService themes,
        LibrarySyncWorker librarySync,
        IMirrorDiscoveryService mirrorDiscovery)
    {
        _settingsService = settingsService;
        _rezka = rezka;
        _images = images;
        _themes = themes;
        _librarySync = librarySync;
        _mirrorDiscovery = mirrorDiscovery;
        Player = player;
        _librarySync.SnapshotChanged += OnLibrarySnapshotChanged;
        _librarySync.SyncFailed += OnLibrarySyncFailed;

        QuickSearches =
        [
            new QuickSearchItem("Новинки", "новинки"),
            new QuickSearchItem("Фантастика", "фантастика"),
            new QuickSearchItem("Детективы", "детектив"),
            new QuickSearchItem("Аниме", "аниме")
        ];

        FilmsMenu = CreateCategoryMenu(
            "films",
            "Фильмы",
            "фильмы",
            ["Вестерны", "Арт-хаус", "Криминал", "Фантастика", "Ужасы", "Документальные", "Познавательные", "Короткометражные"],
            ["Семейные", "Боевики", "Приключения", "Комедии", "Мюзиклы", "Эротика", "Театр", "Русские"],
            ["Фэнтези", "Военные", "Драмы", "Мелодрамы", "Музыкальные", "Детские", "Концерт", "Украинские"],
            ["Биографические", "Детективы", "Спортивные", "Триллеры", "Исторические", "Путешествия", "Стендап", "Зарубежные"]);

        SeriesMenu = CreateCategoryMenu(
            "series",
            "Сериалы",
            "сериалы",
            ["Военные", "Ужасы", "Фэнтези", "Комедии", "Биографические", "Реальное ТВ", "Русские"],
            ["Боевики", "Приключения", "Драмы", "Детективы", "Вестерны", "Телепередачи", "Украинские"],
            ["Арт-хаус", "Семейные", "Мелодрамы", "Криминал", "Документальные", "Стендап", "Зарубежные"],
            ["Триллеры", "Фантастика", "Спортивные", "Исторические", "Музыкальные", "Эротика"]);

        CartoonsMenu = CreateCategoryMenu(
            "cartoons",
            "Мультфильмы",
            "мультфильмы",
            ["Фантастика", "Комедии", "Мелодрамы", "Триллеры", "Сказки", "Спортивные", "Детские", "Полнометражные"],
            ["Фэнтези", "Вестерны", "Арт-хаус", "Исторические", "Семейные", "Познавательные", "Для взрослых", "Советские"],
            ["Боевики", "Военные", "Детективы", "Документальные", "Ужасы", "Мюзиклы", "Мультсериалы", "Русские"],
            ["Биографические", "Драмы", "Криминал", "Эротика", "Приключения", "Аниме", "Короткометражные", "Украинские"]);

        AnimeMenu = CreateCategoryMenu(
            "anime",
            "Аниме",
            "аниме",
            ["Военные", "Комедии", "Романтические", "Музыкальные", "Самурайский боевик", "Пародия", "Кодомо", "Сёнэн-ай"],
            ["Драмы", "Фантастика", "Исторические", "Эротика", "Спортивные", "Школа", "Сёдзё-ай", "Этти"],
            ["Детективы", "Фэнтези", "Ужасы", "Боевики", "Образовательные", "Детские", "Сёдзё", "Махо-сёдзё"],
            ["Триллеры", "Приключения", "Мистические", "Боевые искусства", "Повседневность", "Сказки", "Сёнэн", "Меха"]);

        Initialization = InitializeAsync();
    }

    public Task Initialization { get; }

    public PlayerViewModel Player { get; }

    public IReadOnlyList<QuickSearchItem> QuickSearches { get; }

    public CategoryMenuDefinition FilmsMenu { get; }

    public CategoryMenuDefinition SeriesMenu { get; }

    public CategoryMenuDefinition CartoonsMenu { get; }

    public CategoryMenuDefinition AnimeMenu { get; }

    public bool IsFilmsMenuOpen =>
        IsCategoryMenuOpen && ReferenceEquals(OpenCategoryMenu, FilmsMenu);

    public bool IsSeriesMenuOpen =>
        IsCategoryMenuOpen && ReferenceEquals(OpenCategoryMenu, SeriesMenu);

    public bool IsCartoonsMenuOpen =>
        IsCategoryMenuOpen && ReferenceEquals(OpenCategoryMenu, CartoonsMenu);

    public bool IsAnimeMenuOpen =>
        IsCategoryMenuOpen && ReferenceEquals(OpenCategoryMenu, AnimeMenu);

    public ObservableCollection<MirrorStatusItem> MirrorStatuses { get; } = [];

    public ObservableCollection<MediaCardItem> Results { get; } = [];

    public ObservableCollection<MediaCardItem> Recent { get; } = [];

    public ObservableCollection<MediaCardItem> ContinueWatching { get; } = [];

    public ObservableCollection<LibraryFolderItem> BookmarkFolders { get; } = [];

    public ObservableCollection<TranslationItem> Translations { get; } = [];

    public ObservableCollection<ChoiceItem> Seasons { get; } = [];

    public ObservableCollection<ChoiceItem> Episodes { get; } = [];

    public ObservableCollection<QualityItem> Qualities { get; } = [];

    public ObservableCollection<SubtitleItem> Subtitles { get; } = [];

    [ObservableProperty]
    private bool _isHomeVisible = true;

    [ObservableProperty]
    private bool _isLibraryVisible;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isDetailsVisible;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private bool _isPlaybackOptionsOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isPremium;

    [ObservableProperty]
    private string _accountLabel = "Профиль";

    [ObservableProperty]
    private string _profileName = "Профиль";

    [ObservableProperty]
    private string _profileEmail = string.Empty;

    [ObservableProperty]
    private string _profileInitials = "R";

    [ObservableProperty]
    private Task<Bitmap?> _profileImageSource = Task.FromResult<Bitmap?>(null);

    [ObservableProperty]
    private bool _isProfilePopupOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilmsMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsSeriesMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsCartoonsMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsAnimeMenuOpen))]
    private CategoryMenuDefinition? _openCategoryMenu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilmsMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsSeriesMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsCartoonsMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsAnimeMenuOpen))]
    private bool _isCategoryMenuOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilmsCategory))]
    [NotifyPropertyChangedFor(nameof(IsSeriesCategory))]
    [NotifyPropertyChangedFor(nameof(IsCartoonsCategory))]
    [NotifyPropertyChangedFor(nameof(IsAnimeCategory))]
    private string? _activeCategory;

    public bool IsFilmsCategory => ActiveCategory == "films";

    public bool IsSeriesCategory => ActiveCategory == "series";

    public bool IsCartoonsCategory => ActiveCategory == "cartoons";

    public bool IsAnimeCategory => ActiveCategory == "anime";

    [ObservableProperty]
    private bool _isStartupVisible = true;

    [ObservableProperty]
    private bool _isStartupPresented = true;

    [ObservableProperty]
    private bool _isShellVisible;

    [ObservableProperty]
    private bool _isStartupLoading = true;

    [ObservableProperty]
    private bool _isStartupAuthenticationRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMirrorSelectorEnabled))]
    [NotifyPropertyChangedFor(nameof(IsMirrorNavigationEnabled))]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    private bool _isMirrorCheckRunning = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    private bool _hasAvailableMirror;

    [ObservableProperty]
    private bool _isAutoMirrorSelection = true;

    [ObservableProperty]
    private string _mirrorSelectorLabel = "Проверка подключения";

    [ObservableProperty]
    private string _customMirror = string.Empty;

    [ObservableProperty]
    private string _mirrorStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isMirrorStatusVisible;

    [ObservableProperty]
    private bool _isUsingCustomMirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoginWizardStep))]
    [NotifyPropertyChangedFor(nameof(IsMirrorWizardStep))]
    private int _startupWizardStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMirrorNavigationEnabled))]
    private bool _isWizardTransitioning;

    public bool IsLoginWizardStep => StartupWizardStep == 0;

    public bool IsMirrorWizardStep => StartupWizardStep == 1;

    public bool IsMirrorSelectorEnabled => !IsMirrorCheckRunning;

    public bool IsMirrorNavigationEnabled =>
        IsMirrorSelectorEnabled && !IsWizardTransitioning;

    public bool CanLogin => !IsMirrorCheckRunning && HasAvailableMirror;

    [ObservableProperty]
    private string _startupTitle = "Запускаем Rezui";

    [ObservableProperty]
    private string _startupMessage = "Читаем настройки приложения";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _origin = RezkaMirrors.Primary;

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _loginStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoginStatusVisible;

    [ObservableProperty]
    private bool _isLoginRunning;

    [ObservableProperty]
    private bool _isLoginLogoShifted;

    [ObservableProperty]
    private bool _isLoginFoxVisible;

    [ObservableProperty]
    private bool _areLoginDotsVisible;

    [ObservableProperty]
    private bool _isLoginSuccessTransition;

    [ObservableProperty]
    private bool _isLoginFoxCentered;

    [ObservableProperty]
    private bool _isLoginFoxMotionSuppressed = true;

    [ObservableProperty]
    private ThemePreference _selectedTheme = ThemePreference.System;

    public bool IsSystemTheme => SelectedTheme == ThemePreference.System;

    public bool IsLightTheme => SelectedTheme == ThemePreference.Light;

    public bool IsDarkTheme => SelectedTheme == ThemePreference.Dark;

    partial void OnSelectedThemeChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

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
    private void ShowLibrary()
    {
        ActiveCategory = null;
        Navigate(Page.Library);
        RequestLibraryRefresh(LibrarySyncReason.LibraryOpened);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        StatusMessage = string.Empty;
        Navigate(Page.Settings);
    }

    [RelayCommand]
    private void ToggleProfilePopup()
    {
        IsCategoryMenuOpen = false;
        IsProfilePopupOpen = !IsProfilePopupOpen;
    }

    [RelayCommand]
    private void CloseProfilePopup() =>
        IsProfilePopupOpen = false;

    [RelayCommand]
    private void ToggleCategoryMenu(CategoryMenuDefinition menu)
    {
        IsProfilePopupOpen = false;
        if (IsCategoryMenuOpen && ReferenceEquals(OpenCategoryMenu, menu))
        {
            IsCategoryMenuOpen = false;
            return;
        }

        OpenCategoryMenu = menu;
        IsCategoryMenuOpen = true;
    }

    [RelayCommand]
    private void CloseCategoryMenu() =>
        IsCategoryMenuOpen = false;

    [RelayCommand]
    private async Task LoginAsync()
    {
        ClearLoginStatus();

        if (!CanLogin)
        {
            ShowLoginStatus("Добавьте доступное зеркало, чтобы войти");
            return;
        }

        if (!MailAddress.TryCreate(Login.Trim(), out _) || string.IsNullOrEmpty(Password))
        {
            ShowLoginStatus("Введите корректную почту и пароль");
            return;
        }

        IsLoginRunning = true;

        try
        {
            await PlayLoginCompositionAsync(_lifetimeCancellation.Token);
            await _rezka.ConfigureOriginAsync(Origin);
            _settings = _rezka.Settings;
            var state = await _rezka.LoginAsync(
                Login,
                Password);
            Password = string.Empty;
            await PlayLoginSuccessTransitionAsync(_lifetimeCancellation.Token);
            await CompleteStartupAsync(state);
        }
        catch (Exception exception)
        {
            ShowAuthenticationRequired(ToUserMessage(exception));
        }
    }

    private async Task PlayLoginCompositionAsync(CancellationToken cancellationToken)
    {
        IsLoginLogoShifted = true;
        await Task.Delay(280, cancellationToken);
        IsLoginFoxVisible = true;
        await Task.Delay(200, cancellationToken);
        AreLoginDotsVisible = true;
        await Task.Delay(200, cancellationToken);
    }

    private async Task PlayLoginSuccessTransitionAsync(CancellationToken cancellationToken)
    {
        IsLoginSuccessTransition = true;
        await Task.Delay(TimeSpan.FromMilliseconds(320), cancellationToken);

        IsLoginFoxMotionSuppressed = false;
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        IsLoginFoxCentered = true;
        await Task.Delay(TimeSpan.FromMilliseconds(440), cancellationToken);
    }

    private void ResetLoginComposition()
    {
        IsLoginFoxMotionSuppressed = true;
        IsLoginLogoShifted = false;
        IsLoginFoxVisible = false;
        AreLoginDotsVisible = false;
        IsLoginSuccessTransition = false;
        IsLoginFoxCentered = false;
    }

    [RelayCommand]
    private void SelectAutoMirror()
    {
        IsAutoMirrorSelection = true;
        MirrorSelectorLabel = "Зеркало: автовыбор";
        var fastest = MirrorStatuses
            .Where(item => item.IsAvailable)
            .OrderBy(item => item.LatencyMilliseconds)
            .FirstOrDefault();
        ApplyMirrorSelection(fastest?.Origin);
    }

    [RelayCommand]
    private async Task OpenMirrorWizardAsync()
    {
        if (IsMirrorNavigationEnabled)
        {
            await SetStartupWizardStepAsync(1);
        }
    }

    [RelayCommand]
    private async Task CloseMirrorWizardAsync()
    {
        if (!IsWizardTransitioning)
        {
            await SetStartupWizardStepAsync(0);
        }
    }

    private async Task SetStartupWizardStepAsync(int step)
    {
        if (StartupWizardStep == step)
        {
            return;
        }

        IsWizardTransitioning = true;
        StartupWizardStep = step;
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(320),
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // The window is closing; there is no transition left to unlock.
        }
        finally
        {
            IsWizardTransitioning = false;
        }
    }

    [RelayCommand]
    private void SelectMirror(MirrorStatusItem? mirror)
    {
        if (mirror?.IsAvailable != true)
        {
            return;
        }

        IsAutoMirrorSelection = false;
        MirrorSelectorLabel = $"Зеркало: {mirror.DisplayName}";
        ApplyMirrorSelection(mirror.Origin);
    }

    [RelayCommand]
    private async Task UseCustomMirrorAsync()
    {
        ClearMirrorStatus();
        Uri normalized;
        try
        {
            normalized = RezkaClientService.NormalizeOrigin(CustomMirror);
        }
        catch (ArgumentException)
        {
            ShowMirrorStatus("Введите адрес зеркала, например https://example.com");
            return;
        }

        var origin = normalized.AbsoluteUri.TrimEnd('/');
        IsUsingCustomMirror = true;
        try
        {
            var probe = (await _mirrorDiscovery.ProbeAsync(
                    new[] { origin },
                    _lifetimeCancellation.Token))
                .Single();

            if (!probe.IsAvailable)
            {
                ShowMirrorStatus("Зеркало не отвечает. Проверьте адрес или попробуйте другое");
                return;
            }

            if (!await _mirrorDiscovery.IsRezkaMirrorAsync(
                    origin,
                    _lifetimeCancellation.Token))
            {
                ShowMirrorStatus("Адрес доступен, но не является зеркалом HDRezka");
                return;
            }

            UpsertMirror(probe);
            if (!RezkaMirrors.IsDefault(origin)
                && !_settings.CustomMirrors.Contains(
                    origin,
                    StringComparer.OrdinalIgnoreCase))
            {
                _settings.CustomMirrors.Add(origin);
                await _settingsService.SaveAsync(
                    _settings,
                    _lifetimeCancellation.Token);
            }

            SelectMirror(MirrorStatuses.First(item =>
                string.Equals(
                    item.Origin,
                    probe.Origin,
                    StringComparison.OrdinalIgnoreCase)));
            CustomMirror = string.Empty;
            ShowMirrorStatus("Пользовательское зеркало выбрано");
            StartupWizardStep = 0;
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        finally
        {
            IsUsingCustomMirror = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        IsProfilePopupOpen = false;
        IsCategoryMenuOpen = false;
        ResetLoginComposition();
        var hiddenReset = Task.Delay(
            TimeSpan.FromMilliseconds(220),
            _lifetimeCancellation.Token);

        try
        {
            await _rezka.LogoutAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        catch
        {
            // Logout clears the local session in a finally block. The login
            // screen is still the correct destination if the remote call fails.
        }

        await hiddenReset;
        ApplyAuthentication(null);
        StatusMessage = string.Empty;
        await TransitionToAuthenticationAsync(_lifetimeCancellation.Token);
    }

    private async Task TransitionToAuthenticationAsync(CancellationToken cancellationToken)
    {
        IsStartupPresented = true;
        IsStartupVisible = false;
        IsStartupLoading = false;
        IsStartupAuthenticationRequired = true;
        StartupWizardStep = 0;
        StartupTitle = "Вход в аккаунт";
        StartupMessage = LoginPrompt;
        IsLoginRunning = false;
        ClearLoginStatus();
        Password = string.Empty;

        await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken);
        IsStartupVisible = true;
        await Task.Delay(TimeSpan.FromMilliseconds(360), cancellationToken);
        IsShellVisible = false;
    }

    [RelayCommand]
    private async Task SetThemeAsync(string? value)
    {
        if (!Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var theme))
        {
            return;
        }

        SelectedTheme = theme;
        _settings.Theme = theme;
        _themes.Apply(theme);
        await _settingsService.SaveAsync(_settings);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        ActiveCategory = null;
        await SearchCoreAsync();
    }

    [RelayCommand]
    private async Task BrowseCategoryAsync(string descriptor)
    {
        IsCategoryMenuOpen = false;
        var separatorIndex = descriptor.IndexOf('|');
        if (separatorIndex <= 0 || separatorIndex == descriptor.Length - 1)
        {
            return;
        }

        ActiveCategory = descriptor[..separatorIndex];
        SearchQuery = descriptor[(separatorIndex + 1)..];
        await SearchCoreAsync();
    }

    private async Task SearchCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ShowLibrary();
            return;
        }

        Navigate(Page.Library);
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
        ResetStartupState();
        var cancellationToken = _lifetimeCancellation.Token;
        var minimumLoadingTime = Task.Delay(
            TimeSpan.FromMilliseconds(480),
            cancellationToken);
        try
        {
            SetStartupLoading();
            _settings = await _settingsService.LoadAsync(cancellationToken);
            _rezka.AttachSettings(_settings);
            SelectedTheme = _settings.Theme;
            _themes.Apply(SelectedTheme);
            RebuildRecent();
            SetStartupLoading();
            await RefreshMirrorsAsync(cancellationToken);
            if (!HasAvailableMirror)
            {
                await minimumLoadingTime;
                ShowAuthenticationRequired(
                    "Ни одно встроенное зеркало не отвечает. Добавьте доступный адрес в меню подключения");
                return;
            }

            await _rezka.ConfigureOriginAsync(Origin, cancellationToken);
            _settings = _rezka.Settings;

            SetStartupLoading();
            var state = await _rezka.InitializeAsync(_settings, cancellationToken);
            if (state?.IsAuthenticated != true)
            {
                await minimumLoadingTime;
                ShowAuthenticationRequired();
                return;
            }

            await minimumLoadingTime;
            await CompleteStartupAsync(state);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        catch (Exception exception)
        {
            try
            {
                await minimumLoadingTime;
            }
            catch (OperationCanceledException) when (_disposed)
            {
                return;
            }

            ShowAuthenticationRequired(
                $"{WithoutTrailingPeriod(ToUserMessage(exception))} Выберите другое зеркало и войдите снова");
        }
    }

    private async Task CompleteStartupAsync(AuthenticationState state)
    {
        SetStartupLoading();

        var profile = await _rezka.GetProfileAsync();
        ApplyAuthentication(state, profile);

        SetStartupLoading();
        RebuildRecent();
        Navigate(Page.Home);
        IsShellVisible = true;
        await Task.Delay(TimeSpan.FromMilliseconds(60), _lifetimeCancellation.Token);

        IsStartupVisible = false;
        await Task.Delay(TimeSpan.FromMilliseconds(360), _lifetimeCancellation.Token);
        IsStartupPresented = false;
        StatusMessage = "Сессия восстановлена";
        RequestLibraryRefresh(LibrarySyncReason.SessionRestored);
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

    public void RequestLibraryRefresh(LibrarySyncReason reason)
    {
        if (_disposed || !IsAuthenticated || IsStartupVisible)
        {
            return;
        }

        _librarySync.RequestRefresh(reason);
    }

    private void OnLibrarySnapshotChanged(AccountLibrarySnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                ApplyLibrarySnapshot(snapshot);
            },
            DispatcherPriority.Background);
    }

    private void OnLibrarySyncFailed(Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                if (exception is LoginRequiredException)
                {
                    ShowAuthenticationRequired(
                        "Сессия истекла. Войдите в аккаунт ещё раз");
                }
                else if (IsLibraryVisible)
                {
                    StatusMessage = ToUserMessage(exception);
                }
            },
            DispatcherPriority.Background);
    }

    private void ApplyLibrarySnapshot(AccountLibrarySnapshot snapshot)
    {
        var existingCards = ContinueWatching
            .GroupBy(card => card.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var continueWatching = snapshot.ContinueWatching
            .Select(item => ReuseOrCreateCard(
                existingCards,
                item.Title,
                item.Url,
                item.ImageUrl,
                RezkaClientService.LocalizeCategory(item.Category)))
            .ToArray();
        ReconcileCollection(
            ContinueWatching,
            continueWatching,
            card => card.Url.AbsoluteUri,
            StringComparer.OrdinalIgnoreCase);

        var existingFolders = BookmarkFolders.ToDictionary(
            folder => folder.Name,
            StringComparer.OrdinalIgnoreCase);
        var folders = snapshot.BookmarkFolders
            .Select(folder =>
            {
                existingFolders.TryGetValue(folder.Name, out var existingFolder);
                var folderCards = existingFolder?.Items
                    .GroupBy(card => card.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First(),
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, MediaCardItem>(
                        StringComparer.OrdinalIgnoreCase);
                var cards = folder.Items
                    .Select(item => ReuseOrCreateCard(
                        folderCards,
                        item.Title,
                        item.Url,
                        item.ImageUrl,
                        RezkaClientService.LocalizeCategory(item.Category)))
                    .ToArray();

                return existingFolder is not null &&
                       existingFolder.ItemCount == folder.ItemCount &&
                       existingFolder.Items.SequenceEqual(cards)
                    ? existingFolder
                    : new LibraryFolderItem(folder.Name, folder.ItemCount, cards);
            })
            .ToArray();
        ReconcileCollection(
            BookmarkFolders,
            folders,
            folder => folder.Name,
            StringComparer.OrdinalIgnoreCase);

        if (IsLibraryVisible)
        {
            StatusMessage = ContinueWatching.Count == 0 && BookmarkFolders.Count == 0
                ? "В библиотеке пока ничего нет"
                : "Библиотека синхронизирована";
        }
    }

    private MediaCardItem ReuseOrCreateCard(
        IReadOnlyDictionary<string, MediaCardItem> existing,
        string title,
        Uri url,
        Uri? imageUrl,
        string category)
    {
        if (existing.TryGetValue(url.AbsoluteUri, out var card) &&
            string.Equals(card.Title, title, StringComparison.Ordinal) &&
            string.Equals(card.Category, category, StringComparison.Ordinal))
        {
            return card;
        }

        return CreateCard(title, url, imageUrl, category);
    }

    private static void ReconcileCollection<T, TKey>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var desiredItem = desired[index];
            var desiredKey = keySelector(desiredItem);
            var existingIndex = -1;
            for (var candidate = index; candidate < target.Count; candidate++)
            {
                if (comparer.Equals(keySelector(target[candidate]), desiredKey))
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                target.Insert(index, desiredItem);
                continue;
            }

            if (existingIndex != index)
            {
                target.Move(existingIndex, index);
            }

            if (!ReferenceEquals(target[index], desiredItem))
            {
                target[index] = desiredItem;
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void ApplyAuthentication(
        AuthenticationState? state,
        AccountProfile? profile = null)
    {
        var profileEmail = profile?.Email?.Trim() ?? string.Empty;
        var profileName = profile?.Username?.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            var separatorIndex = profileEmail.IndexOf('@');
            profileName = separatorIndex > 0
                ? profileEmail[..separatorIndex]
                : "Профиль";
        }

        IsAuthenticated = state?.IsAuthenticated == true;
        IsPremium = profile?.IsPremium ?? state?.IsPremium == true;
        ProfileName = profileName;
        ProfileEmail = profileEmail;
        ProfileInitials = GetInitials(ProfileName);
        ProfileImageSource = _images.LoadAsync(profile?.AvatarUrl, _rezka.Origin);
        AccountLabel = IsPremium ? "Premium" : ProfileName;
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
        catch (LoginRequiredException)
        {
            ShowAuthenticationRequired(
                "Сессия истекла. Войдите в аккаунт ещё раз");
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
        IsProfilePopupOpen = false;
        IsCategoryMenuOpen = false;
        if (page != Page.Library)
        {
            ActiveCategory = null;
        }

        IsHomeVisible = page == Page.Home;
        IsLibraryVisible = page == Page.Library;
        IsSettingsVisible = page == Page.Settings;
        IsDetailsVisible = page == Page.Details;
        IsPlayerVisible = page == Page.Player;
    }

    private void ResetStartupState()
    {
        IsProfilePopupOpen = false;
        IsCategoryMenuOpen = false;
        IsStartupPresented = true;
        IsStartupVisible = true;
        IsShellVisible = false;
        IsStartupLoading = true;
        IsStartupAuthenticationRequired = false;
        MirrorStatuses.Clear();
        IsMirrorCheckRunning = true;
        HasAvailableMirror = false;
        IsAutoMirrorSelection = true;
        StartupWizardStep = 0;
        IsLoginRunning = false;
        ClearLoginStatus();
        ResetLoginComposition();
        MirrorSelectorLabel = "Проверка подключения";
        ClearMirrorStatus();
        StatusMessage = string.Empty;
    }

    private void SetStartupLoading()
    {
        IsStartupPresented = true;
        IsStartupVisible = true;
        IsShellVisible = false;
        IsStartupLoading = true;
        IsStartupAuthenticationRequired = false;
    }

    private void ShowAuthenticationRequired(string? statusMessage = null)
    {
        IsProfilePopupOpen = false;
        IsCategoryMenuOpen = false;
        IsStartupPresented = true;
        IsStartupVisible = true;
        IsShellVisible = false;
        IsStartupLoading = false;
        IsStartupAuthenticationRequired = true;
        StartupWizardStep = 0;
        StartupTitle = "Вход в аккаунт";
        StartupMessage = LoginPrompt;
        IsLoginRunning = false;
        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            ClearLoginStatus();
        }
        else
        {
            ShowLoginStatus(statusMessage);
        }

        ResetLoginComposition();
        Password = string.Empty;
    }

    private void ShowLoginStatus(string message)
    {
        CancelLoginStatusDismissal();
        LoginStatusMessage = WithoutTrailingPeriod(message);
        IsLoginStatusVisible = true;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _loginStatusCancellation = cancellation;
        _ = HideLoginStatusAsync(cancellation);
    }

    private async Task HideLoginStatusAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token);
            if (!ReferenceEquals(_loginStatusCancellation, cancellation))
            {
                return;
            }

            IsLoginStatusVisible = false;
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellation.Token);
            if (ReferenceEquals(_loginStatusCancellation, cancellation))
            {
                LoginStatusMessage = string.Empty;
                _loginStatusCancellation = null;
                cancellation.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer message or a screen reset superseded this dismissal.
        }
    }

    private void ClearLoginStatus()
    {
        CancelLoginStatusDismissal();
        IsLoginStatusVisible = false;
        LoginStatusMessage = string.Empty;
    }

    private void CancelLoginStatusDismissal()
    {
        var cancellation = _loginStatusCancellation;
        _loginStatusCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private static string WithoutTrailingPeriod(string message) =>
        message.Trim().TrimEnd('.');

    private void ShowMirrorStatus(string message)
    {
        CancelMirrorStatusDismissal();
        MirrorStatusMessage = WithoutTrailingPeriod(message);
        IsMirrorStatusVisible = true;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _mirrorStatusCancellation = cancellation;
        _ = HideMirrorStatusAsync(cancellation);
    }

    private async Task HideMirrorStatusAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token);
            if (!ReferenceEquals(_mirrorStatusCancellation, cancellation))
            {
                return;
            }

            IsMirrorStatusVisible = false;
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellation.Token);
            if (ReferenceEquals(_mirrorStatusCancellation, cancellation))
            {
                MirrorStatusMessage = string.Empty;
                _mirrorStatusCancellation = null;
                cancellation.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer message or a screen reset superseded this dismissal.
        }
    }

    private void ClearMirrorStatus()
    {
        CancelMirrorStatusDismissal();
        IsMirrorStatusVisible = false;
        MirrorStatusMessage = string.Empty;
    }

    private void CancelMirrorStatusDismissal()
    {
        var cancellation = _mirrorStatusCancellation;
        _mirrorStatusCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task RefreshMirrorsAsync(CancellationToken cancellationToken)
    {
        IsMirrorCheckRunning = true;
        MirrorSelectorLabel = "Проверка подключения";
        try
        {
            var candidates = RezkaMirrors.Defaults
                .Concat(_settings.CustomMirrors)
                .Append(_settings.Origin)
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var probes = await _mirrorDiscovery.ProbeAsync(
                candidates,
                cancellationToken);

            MirrorStatuses.Clear();
            foreach (var probe in probes)
            {
                UpsertMirror(probe);
            }
        }
        finally
        {
            IsMirrorCheckRunning = false;
        }

        var sessionMirror = FindRestorableSessionMirror(_settings, MirrorStatuses);
        if (sessionMirror is not null)
        {
            IsAutoMirrorSelection = false;
            MirrorSelectorLabel = $"Зеркало: {sessionMirror.DisplayName}";
            ApplyMirrorSelection(sessionMirror.Origin);
        }
        else
        {
            SelectAutoMirror();
        }
    }

    internal static MirrorStatusItem? FindRestorableSessionMirror(
        AppSettings settings,
        IEnumerable<MirrorStatusItem> mirrors)
    {
        if (!settings.RememberSession
            || settings.AuthenticationCookies.Count == 0
            || string.IsNullOrWhiteSpace(settings.Origin))
        {
            return null;
        }

        return mirrors.FirstOrDefault(mirror =>
            mirror.IsAvailable
            && string.Equals(
                mirror.Origin,
                settings.Origin,
                StringComparison.OrdinalIgnoreCase));
    }

    private void UpsertMirror(MirrorProbeResult probe)
    {
        var existingIndex = -1;
        for (var index = 0; index < MirrorStatuses.Count; index++)
        {
            if (string.Equals(
                    MirrorStatuses[index].Origin,
                    probe.Origin,
                    StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = index;
                break;
            }
        }

        var item = new MirrorStatusItem(
            probe.Origin,
            probe.DisplayName,
            probe.LatencyMilliseconds,
            probe.IsAvailable,
            !RezkaMirrors.IsDefault(probe.Origin),
            false);
        if (existingIndex < 0)
        {
            MirrorStatuses.Add(item);
        }
        else
        {
            MirrorStatuses[existingIndex] = item;
        }

        HasAvailableMirror = MirrorStatuses.Any(mirror => mirror.IsAvailable);
    }

    private void ApplyMirrorSelection(string? origin)
    {
        Origin = origin ?? string.Empty;
        for (var index = 0; index < MirrorStatuses.Count; index++)
        {
            var item = MirrorStatuses[index];
            var selected = !IsAutoMirrorSelection
                && origin is not null
                && string.Equals(
                    item.Origin,
                    origin,
                    StringComparison.OrdinalIgnoreCase);
            if (item.IsSelected != selected)
            {
                item.IsSelected = selected;
            }
        }
    }

    private static CategoryMenuDefinition CreateCategoryMenu(
        string category,
        string title,
        string searchNoun,
        params string[][] columns)
    {
        var menuColumns = columns
            .Select(column => new CategoryMenuColumn(
                column
                    .Select(item => new CategoryMenuItem(
                        item,
                        $"{category}|{item.ToLowerInvariant()} {searchNoun}"))
                    .ToArray()))
            .ToArray();

        return new CategoryMenuDefinition(
            title,
            $"Все {searchNoun}",
            $"{category}|{searchNoun}",
            $"{category}|новинки {searchNoun}",
            menuColumns);
    }

    private static string GetInitials(string value)
    {
        var parts = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();
        return parts.Length == 0 ? "R" : new string(parts);
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        LoginFailedException => "Не удалось войти: проверьте почту и пароль",
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _librarySync.SnapshotChanged -= OnLibrarySnapshotChanged;
        _librarySync.SyncFailed -= OnLibrarySyncFailed;
        CancelLoginStatusDismissal();
        CancelMirrorStatusDismissal();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private enum Page
    {
        Home,
        Library,
        Settings,
        Details,
        Player
    }
}

public sealed record QuickSearchItem(string Title, string Query);
