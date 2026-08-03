using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Mail;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HdRezka;
using Rezui.Models;
using Rezui.Services;
using Serilog;

namespace Rezui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int DetailsLoadMaximumAttempts = 4;
    private const int HomeEntranceAnimationMilliseconds = 620;
    private const int DetailsEntranceAnimationMilliseconds = 780;
    private const int DetailsHeroMetadataMinimumVisibleMilliseconds = 900;
    private const int DetailsHeroHeadingTransitionMilliseconds = 560;
    private const double DetailsHeroTitleWidth = 720;
    private const double DetailsHeroTitleTargetLines = 1.56;
    private const double DetailsHeroTitleMinimumFontSize = 28;
    private const double DetailsHeroTitleMaximumFontSize = 38;

    private const string LoginPrompt =
        "Войдите в приложение используя свой персональный аккаунт HDRezka";

    private readonly SettingsService _settingsService;
    private readonly RezkaClientService _rezka;
    private readonly ImageCacheService _images;
    private readonly ThemeService _themes;
    private readonly LibrarySyncWorker _librarySync;
    private readonly IMirrorDiscoveryService _mirrorDiscovery;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _detailsLoadCancellation;
    private CancellationTokenSource? _pageNavigationCancellation;
    private CancellationTokenSource? _loginStatusCancellation;
    private CancellationTokenSource? _mirrorStatusCancellation;
    private AppSettings _settings = new();
    private AuthState _auth = new();
    private Media? _media;
    private MediaStream? _resolvedStream;
    private string? _resolvedSelectionKey;
    private bool _suppressSeasonChange;
    private int _detailsRequestVersion;
    private int _detailsImageVersion;
    private int _detailsImageUpgradeVersion;
    private int _pageTransitionVersion;
    private bool _hasDetailsPrimaryImageSource;
    private Task<Bitmap?> _detailsPrimaryImageTask = Task.FromResult<Bitmap?>(null);
    private Task<Bitmap?> _detailsUpgradeImageTask = Task.FromResult<Bitmap?>(null);
    private DetailsOpenRequest? _detailsOpenRequest;
    private Page _currentPage = Page.Home;
    private volatile bool _disposed;

    public MainWindowViewModel(
        SettingsService settingsService,
        RezkaClientService rezka,
        ImageCacheService images,
        PlayerViewModel player,
        ThemeService themes,
        LibrarySyncWorker librarySync,
        IMirrorDiscoveryService mirrorDiscovery,
        ILogger? logger = null)
    {
        _settingsService = settingsService;
        _rezka = rezka;
        _images = images;
        _themes = themes;
        _librarySync = librarySync;
        _mirrorDiscovery = mirrorDiscovery;
        _logger = logger ?? Log.ForContext<MainWindowViewModel>();
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinueWatchingHero))]
    private ContinueWatchingHeroItem? _continueWatchingHero;

    public bool HasContinueWatchingHero => ContinueWatchingHero is not null;

    public ObservableCollection<LibraryFolderItem> BookmarkFolders { get; } = [];

    public ObservableCollection<TranslationItem> Translations { get; } = [];

    public ObservableCollection<ChoiceItem> Seasons { get; } = [];

    public ObservableCollection<ChoiceItem> Episodes { get; } = [];

    public ObservableCollection<QualityItem> Qualities { get; } = [];

    public ObservableCollection<SubtitleItem> Subtitles { get; } = [];

    [ObservableProperty]
    private bool _isHomeVisible = true;

    [ObservableProperty]
    private bool _isHomeEntering;

    [ObservableProperty]
    private bool _isHomeLeaving;

    [ObservableProperty]
    private bool _isLibraryVisible;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isDetailsVisible;

    [ObservableProperty]
    private bool _isDetailsEntering;

    [ObservableProperty]
    private bool _isDetailsLeaving;

    [ObservableProperty]
    private bool _isDetailsContentLoading;

    [ObservableProperty]
    private bool _isDetailsContentReady;

    [ObservableProperty]
    private bool _isDetailsContentLoadFailed;

    [ObservableProperty]
    private string _detailsContentError = string.Empty;

    [ObservableProperty]
    private string _detailsLoadingStatus = string.Empty;

    [ObservableProperty]
    private bool _isDetailsHeroBackgroundReady;

    [ObservableProperty]
    private bool _isDetailsHeroSurfaceVisible;

    [ObservableProperty]
    private bool _isDetailsHeroMetadataReady;

    [ObservableProperty]
    private bool _isDetailsHeroMetadataVisible;

    [ObservableProperty]
    private bool _isDetailsHeroContentVisible;

    [ObservableProperty]
    private bool _useDetailedHeroEntrance;

    [ObservableProperty]
    private bool _canWatchDetails;

    [ObservableProperty]
    private bool _isPlayerVisible;

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
    [NotifyPropertyChangedFor(nameof(DetailsTitleFontSize))]
    [NotifyPropertyChangedFor(nameof(DetailsTitleLineHeight))]
    private string _detailsTitle = string.Empty;

    public double DetailsTitleFontSize => CalculateDetailsTitleFontSize(DetailsTitle);

    public double DetailsTitleLineHeight => Math.Round(DetailsTitleFontSize * 1.1, 1);

    [ObservableProperty]
    private string _detailsOriginalTitle = string.Empty;

    [ObservableProperty]
    private string _detailsDescription = string.Empty;

    [ObservableProperty]
    private string _detailsTagline = string.Empty;

    [ObservableProperty]
    private string _detailsMeta = string.Empty;

    [ObservableProperty]
    private string _detailsRating = string.Empty;

    public ObservableCollection<DetailFactItem> DetailsFacts { get; } = [];

    public ObservableCollection<string> DetailsGenres { get; } = [];

    public ObservableCollection<PersonCardItem> DetailsDirectors { get; } = [];

    public ObservableCollection<PersonCardItem> DetailsCast { get; } = [];

    public ObservableCollection<ExternalRatingItem> DetailsExternalRatings { get; } = [];

    public ObservableCollection<ScheduleCardItem> DetailsSchedule { get; } = [];

    public ObservableCollection<MediaCardItem> DetailsRecommendations { get; } = [];

    public ObservableCollection<CommentNodeItem> DetailsComments { get; } = [];

    public ObservableCollection<string> DetailsCollections { get; } = [];

    public ObservableCollection<string> DetailsRankings { get; } = [];

    public ObservableCollection<string> DetailsOtherParts { get; } = [];

    public ObservableCollection<DetailGroupCardItem> DetailsGroups { get; } = [];

    [ObservableProperty]
    private bool _hasDetailGroups;

    [ObservableProperty]
    private int _detailsGroupColumnCount = 1;

    [ObservableProperty]
    private int _commentsPage;

    // Maps comment id -> node across paginated loads so replies arriving on a
    // later page can find their parent built on an earlier one.
    private Dictionary<long, CommentNodeItem> _commentNodeIndex = [];

    [ObservableProperty]
    private int _commentsTotalPages;

    [ObservableProperty]
    private bool _isCommentsLoading;

    public bool CanLoadMoreComments =>
        !IsCommentsLoading && CommentsPage > 0 && CommentsPage < CommentsTotalPages;

    partial void OnIsCommentsLoadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanLoadMoreComments));

    partial void OnCommentsPageChanged(int value) =>
        OnPropertyChanged(nameof(CanLoadMoreComments));

    partial void OnCommentsTotalPagesChanged(int value) =>
        OnPropertyChanged(nameof(CanLoadMoreComments));

    [ObservableProperty]
    private Bitmap? _detailsImageSource;

    [ObservableProperty]
    private Bitmap? _detailsHeroUpgradeImageSource;

    [ObservableProperty]
    private bool _isDetailsHeroImageUpgradeReady;

    [ObservableProperty]
    private bool _isDetailsHeroPrimaryImageVisible = true;

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
        if (value is null || _media is null || _suppressSeasonChange)
        {
            SelectedEpisode = null;
            return;
        }

        _ = LoadEpisodesForSeasonAsync(value.Value);
    }

    [RelayCommand]
    private async Task ShowHomeAsync() =>
        await NavigateAsync(Page.Home);

    [RelayCommand]
    private async Task ShowLibraryAsync()
    {
        ActiveCategory = null;
        await NavigateAsync(Page.Library);
        RequestLibraryRefresh(LibrarySyncReason.LibraryOpened);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        StatusMessage = string.Empty;
        await NavigateAsync(Page.Settings);
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
    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogging.LogDirectory,
                UseShellExecute = true
            });
            _logger.Information(
                "Opened log directory {LogDirectory}",
                AppLogging.LogDirectory);
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Could not open log directory {LogDirectory}",
                AppLogging.LogDirectory);
            StatusMessage = $"Логи находятся в {AppLogging.LogDirectory}";
        }
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
            await ShowLibraryAsync();
            return;
        }

        await NavigateAsync(Page.Library);
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
    private async Task OpenPlayerAsync()
    {
        if (_media is null || !CanWatchDetails || !_media.Playback.IsAvailable)
        {
            return;
        }

        await NavigateAsync(Page.Player);
        await ApplyPlaybackSelectionAsync();
    }

    [RelayCommand]
    private async Task ApplyPlaybackSelectionAsync()
    {
        var selectionKey = BuildPlaybackSelectionKey();
        if (_resolvedStream is null ||
            !string.Equals(_resolvedSelectionKey, selectionKey, StringComparison.Ordinal))
        {
            await ResolvePlaybackAsync();
        }

        if (SelectedQuality is { IsAvailable: true, Urls.Count: > 0 })
        {
            await StartPlaybackAsync();
        }
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
            _resolvedSelectionKey = BuildPlaybackSelectionKey();

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
        });
    }

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
            await NavigateAsync(Page.Player);
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
    private async Task BackFromPlayerAsync()
    {
        Player.StopCommand.Execute(null);
        await NavigateAsync(Page.Details, animateEntrance: false);
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
            StartupMessage = "Читаем настройки приложения";
            SetStartupLoading();
            _settings = await _settingsService.LoadAsync(cancellationToken);
            _auth = await _settingsService.LoadAuthAsync(cancellationToken);
            _rezka.AttachSettings(_settings);
            _rezka.AttachAuth(_auth);
            SelectedTheme = _settings.Theme;
            _themes.Apply(SelectedTheme);
            await RebuildRecentAsync(cancellationToken);

            StartupMessage = "Проверяем доступные зеркала";
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

            StartupMessage = "Восстанавливаем сессию";
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
        var cancellationToken = _lifetimeCancellation.Token;
        StartupMessage = "Загружаем профиль и библиотеку";
        SetStartupLoading();

        var profileTask = _rezka.GetProfileAsync(cancellationToken);
        var libraryTask = _rezka.GetLibraryAsync(cancellationToken);
        await Task.WhenAll(profileTask, libraryTask);

        var profile = await profileTask;
        ApplyAuthentication(state, profile);
        ApplyLibrarySnapshot(await libraryTask);

        StartupMessage = "Подготавливаем главную страницу";
        SetStartupLoading();
        await RebuildRecentAsync(cancellationToken);
        await PrepareHomeAssetsAsync(cancellationToken);

        ShowPage(Page.Home);
        IsShellVisible = true;
        await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken);

        IsStartupVisible = false;
        await Task.Delay(TimeSpan.FromMilliseconds(360), cancellationToken);
        IsStartupPresented = false;
        StatusMessage = "Сессия восстановлена";
    }

    private async Task PrepareHomeAssetsAsync(CancellationToken cancellationToken)
    {
        var imageTasks = new List<Task<Bitmap?>>
        {
            ProfileImageSource
        };

        if (ContinueWatchingHero is { } hero)
        {
            imageTasks.Add(hero.ImageSource.Value);
            imageTasks.Add(hero.BackgroundImageSource.Value);
        }

        await Task.WhenAll(imageTasks.Distinct()).WaitAsync(cancellationToken);
    }

    private async Task OpenMediaAsync(
        Uri url,
        string previewTitle,
        Uri? previewImageUrl,
        DeferredImageSource previewImageSource,
        string previewCategory)
    {
        _detailsOpenRequest = new DetailsOpenRequest(
            url,
            previewTitle,
            previewImageUrl,
            previewImageSource,
            previewCategory);
        CancelDetailsLoad();
        var requestVersion = ++_detailsRequestVersion;
        var detailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _detailsLoadCancellation = detailsCancellation;
        var detailsToken = detailsCancellation.Token;

        try
        {
            _logger.Information(
                "Opening title {MediaHost}{MediaPath}",
                url.Host,
                url.AbsolutePath);
            if (IsDetailsVisible)
            {
                await PlayPageExitAsync(Page.Details, detailsToken);
                detailsToken.ThrowIfCancellationRequested();
                if (requestVersion != _detailsRequestVersion)
                {
                    return;
                }

                IsDetailsVisible = false;
                IsDetailsLeaving = false;
            }

            IsDetailsEntering = false;
            ResetDetailsPresentation();
            _media = null;
            _resolvedStream = null;
            _resolvedSelectionKey = null;
            Translations.Clear();
            Seasons.Clear();
            Episodes.Clear();
            Qualities.Clear();
            Subtitles.Clear();
            SelectedTranslation = null;
            SelectedSeason = null;
            SelectedEpisode = null;
            SelectedQuality = null;
            SelectedSubtitle = null;
            DetailsComments.Clear();
            CommentsPage = 0;
            CommentsTotalPages = 0;
            var cached = await _rezka.GetCachedMediaMetadataAsync(
                url,
                detailsToken);
            detailsToken.ThrowIfCancellationRequested();
            if (cached is not null)
            {
                _logger.Debug(
                    "Presenting cached title metadata with simplified hero entrance for {MediaPath}",
                    url.AbsolutePath);
                UseDetailedHeroEntrance = false;
                IsDetailsContentLoading = false;
                IsDetailsContentReady = true;
                IsDetailsContentLoadFailed = false;
                ApplyMediaMetadata(cached, requestVersion, detailsToken);
                IsDetailsHeroMetadataReady = true;
                IsDetailsHeroMetadataVisible = true;
                await PresentDetailsAsync(requestVersion, detailsToken);
                StatusMessage = string.Empty;
            }
            else
            {
                _logger.Debug(
                    "Presenting uncached title preview with detailed hero entrance for {MediaPath}",
                    url.AbsolutePath);
                UseDetailedHeroEntrance = true;
                IsDetailsContentLoading = true;
                IsDetailsContentReady = false;
                IsDetailsContentLoadFailed = false;
                DetailsLoadingStatus = "Загружаем данные тайтла";
                ApplyMediaPreview(
                    previewTitle,
                    previewImageUrl,
                    previewImageSource,
                    previewCategory,
                    requestVersion,
                    detailsToken);
                await PresentDetailsAsync(requestVersion, detailsToken);
            }

            await RunBusyAsync(async cancellationToken =>
            {
                var maximumAttempts = cached is null
                    ? DetailsLoadMaximumAttempts
                    : 1;
                for (var attempt = 1; attempt <= maximumAttempts; attempt++)
                {
                    if (attempt > 1)
                    {
                        DetailsLoadingStatus =
                            $"Повторная попытка {attempt} из {maximumAttempts}";
                        await Task.Delay(
                            GetDetailsRetryDelay(attempt),
                            cancellationToken);
                    }

                    try
                    {
                        await LoadDetailsContentAttemptAsync(
                            url,
                            previewTitle,
                            requestVersion,
                            cancellationToken);
                        return;
                    }
                    catch (OperationCanceledException) when (
                        !cancellationToken.IsCancellationRequested &&
                        attempt < maximumAttempts)
                    {
                        _logger.Warning(
                            "Title load timed out for {MediaPath}; retrying attempt {NextAttempt} of {MaximumAttempts}",
                            url.AbsolutePath,
                            attempt + 1,
                            maximumAttempts);
                    }
                    catch (Exception exception) when (
                        IsTransientDetailsLoadException(exception) &&
                        attempt < maximumAttempts)
                    {
                        _logger.Warning(
                            exception,
                            "Transient title load failure for {MediaPath}; retrying attempt {NextAttempt} of {MaximumAttempts}",
                            url.AbsolutePath,
                            attempt + 1,
                            maximumAttempts);
                    }
                    catch (OperationCanceledException) when (
                        !detailsToken.IsCancellationRequested)
                    {
                        SetDetailsContentFailure(
                            "Загрузка была отменена",
                            requestVersion);
                        throw;
                    }
                    catch (Exception exception) when (
                        exception is not OperationCanceledException)
                    {
                        _logger.Error(
                            exception,
                            "Title load failed for {MediaPath}",
                            url.AbsolutePath);
                        SetDetailsContentFailure(
                            ToUserMessage(exception),
                            requestVersion);
                        throw;
                    }
                }
            }, showActivity: false, cancellationToken: detailsToken);
        }
        catch (OperationCanceledException) when (detailsToken.IsCancellationRequested)
        {
            _logger.Debug(
                "Title opening cancelled for {MediaPath} because navigation changed",
                url.AbsolutePath);
        }
        finally
        {
            if (ReferenceEquals(_detailsLoadCancellation, detailsCancellation))
            {
                _detailsLoadCancellation = null;
            }

            detailsCancellation.Dispose();
        }
    }

    private async Task PresentDetailsAsync(
        int requestVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestVersion != _detailsRequestVersion)
        {
            return;
        }

        await NavigateAsync(Page.Details, cancellationToken);
    }

    private void ResetDetailsPresentation()
    {
        _detailsImageVersion++;
        _detailsImageUpgradeVersion++;
        _hasDetailsPrimaryImageSource = false;
        IsDetailsContentLoading = false;
        IsDetailsContentReady = false;
        IsDetailsContentLoadFailed = false;
        DetailsContentError = string.Empty;
        DetailsLoadingStatus = string.Empty;
        CanWatchDetails = false;
        IsDetailsHeroBackgroundReady = false;
        IsDetailsHeroImageUpgradeReady = false;
        IsDetailsHeroPrimaryImageVisible = true;
        IsDetailsHeroSurfaceVisible = false;
        IsDetailsHeroMetadataReady = false;
        IsDetailsHeroMetadataVisible = false;
        IsDetailsHeroContentVisible = false;
        UseDetailedHeroEntrance = false;
        DetailsTitle = string.Empty;
        DetailsOriginalTitle = string.Empty;
        DetailsDescription = string.Empty;
        DetailsTagline = string.Empty;
        DetailsMeta = string.Empty;
        DetailsRating = string.Empty;
        _detailsPrimaryImageTask = Task.FromResult<Bitmap?>(null);
        _detailsUpgradeImageTask = Task.FromResult<Bitmap?>(null);
        DetailsImageSource = null;
        DetailsHeroUpgradeImageSource = null;
        _detailsImageUrl = null;
        DetailsFacts.Clear();
        DetailsGenres.Clear();
        DetailsDirectors.Clear();
        DetailsCast.Clear();
        DetailsExternalRatings.Clear();
        DetailsSchedule.Clear();
        DetailsRecommendations.Clear();
        DetailsComments.Clear();
        DetailsCollections.Clear();
        DetailsRankings.Clear();
        DetailsOtherParts.Clear();
        DetailsGroups.Clear();
        DetailsGroupColumnCount = 1;
        HasDetailGroups = false;
        CommentsPage = 0;
        CommentsTotalPages = 0;
    }

    [RelayCommand]
    private async Task RetryDetailsContentAsync()
    {
        if (_detailsOpenRequest is not { } request || IsDetailsContentLoading)
        {
            return;
        }

        await OpenMediaAsync(
            request.Url,
            request.PreviewTitle,
            request.PreviewImageUrl,
            request.PreviewImageSource,
            request.PreviewCategory);
    }

    private void SetDetailsContentFailure(string message, int requestVersion)
    {
        if (requestVersion != _detailsRequestVersion || IsDetailsContentReady)
        {
            return;
        }

        IsDetailsContentLoading = false;
        IsDetailsContentLoadFailed = true;
        DetailsContentError = string.IsNullOrWhiteSpace(message)
            ? "Не удалось загрузить данные тайтла"
            : message;
        DetailsLoadingStatus = string.Empty;
    }

    private async Task LoadDetailsContentAttemptAsync(
        Uri url,
        string previewTitle,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        _media = await Task.Run(
            () => _rezka.LoadMediaAsync(
                url,
                cancellationToken,
                previewTitle),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = await Task.Run(
            () => RezkaClientService.CreateMetadataSnapshot(
                _media,
                previewTitle),
            cancellationToken);
        ApplyMediaMetadata(metadata, requestVersion, cancellationToken);
        IsSeries = _media.Format == MediaFormat.Series;
        CanWatchDetails = _media.Playback.IsAvailable;

        Translations.Clear();
        foreach (var translator in _media.TranslationOptions)
        {
            Translations.Add(new TranslationItem(
                translator.Id,
                translator.Name,
                translator.IsPremium));
        }

        SelectedTranslation =
            Translations.FirstOrDefault(item =>
                !item.IsPremium || _media.IsPremiumAccount)
            ?? Translations.FirstOrDefault();

        Seasons.Clear();
        Episodes.Clear();
        var commentsTask = LoadCommentsPageAsync(
            1,
            replace: true,
            cancellationToken);
        if (IsSeries && CanWatchDetails)
        {
            var media = _media;
            var seasons = await LoadSeriesInfoResilientAsync(
                media,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // A null result means every translation is Premium-protected: keep
            // the metadata visible but leave the season/episode pickers empty
            // instead of failing the whole details view.
            if (seasons is not null)
            {
                foreach (var season in seasons)
                {
                    Seasons.Add(new ChoiceItem(season.Number, season.Title));
                }

                _suppressSeasonChange = true;
                SelectedSeason = Seasons.FirstOrDefault();
                _suppressSeasonChange = false;
                if (SelectedSeason is { } selectedSeason)
                {
                    var season = seasons.FirstOrDefault(item =>
                        item.Number == selectedSeason.Value);
                    if (season is not null)
                    {
                        foreach (var episode in season.Episodes)
                        {
                            Episodes.Add(new ChoiceItem(
                                episode.Number,
                                episode.Title));
                        }
                    }

                    SelectedEpisode = Episodes.FirstOrDefault();
                }
            }
        }

        await commentsTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (requestVersion == _detailsRequestVersion)
        {
            IsDetailsContentLoading = false;
            IsDetailsContentReady = true;
            IsDetailsContentLoadFailed = false;
            DetailsContentError = string.Empty;
            DetailsLoadingStatus = string.Empty;
        }

        await _rezka.SaveRecentAsync(
            _media.Name,
            _media.Url,
            _detailsImageUrl,
            _media.Category,
            cancellationToken);
        await RebuildRecentAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        StatusMessage = string.Empty;
    }

    private static TimeSpan GetDetailsRetryDelay(int attempt) => attempt switch
    {
        2 => TimeSpan.FromMilliseconds(650),
        3 => TimeSpan.FromMilliseconds(1400),
        _ => TimeSpan.FromMilliseconds(2800)
    };

    private static bool IsTransientDetailsLoadException(Exception exception) =>
        exception is HttpRequestException or HttpException or IOException or TimeoutException;

    /// <summary>
    /// Loads seasons and episodes for the details view, tolerating translators
    /// the website marks as Premium-protected. The library's all-translators
    /// <see cref="Media.GetEpisodesInfoAsync(CancellationToken)"/> aggregates
    /// every translator with <c>Task.WhenAll</c>, so a single translator whose
    /// response carries <c>premium_content</c> aborts the whole load with
    /// <see cref="PremiumRequiredException"/> even when free translations exist.
    /// For listing seasons/episodes we treat that as soft: fall back to the
    /// non-premium translators one by one and merge what they provide.
    /// </summary>
    /// <returns>
    /// Seasons merged across non-premium translators, or <see langword="null"/>
    /// when none of them offered any data (genuinely Premium-only content).
    /// </returns>
    private static async Task<IReadOnlyList<Season>?> LoadSeriesInfoResilientAsync(
        Media media,
        CancellationToken cancellationToken)
    {
        try
        {
            return await media.GetEpisodesInfoAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PremiumRequiredException) when (
            media.TranslationOptions.Count != 0)
        {
            // Aggregated load was vetoed by a Premium-marked translator.
            // Rebuild the season/episode list from the remaining translators.
        }

        var candidates = media.TranslationOptions
            .Where(translator => !translator.IsPremium || media.IsPremiumAccount)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var perTranslator = new List<IReadOnlyDictionary<int, SeriesInfo>>();
        foreach (var translator in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var series = await media.GetSeriesInfoAsync(
                    translator.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken).ConfigureAwait(false);
                if (series is not null)
                {
                    perTranslator.Add(new Dictionary<int, SeriesInfo>
                    {
                        [translator.Id] = series,
                    });
                }
            }
            catch (PremiumRequiredException)
            {
                // This translator is Premium-locked too; skip it and keep going.
            }
        }

        return perTranslator.Count == 0
            ? null
            : MergeSeriesInfo(perTranslator);
    }

    /// <summary>
    /// Merges per-translator season/episode maps into a single ordered list,
    /// mirroring how the library combines translations across translators.
    /// Extracted from <see cref="LoadSeriesInfoResilientAsync"/> so the merge
    /// logic can be unit-tested without network access.
    /// </summary>
    internal static IReadOnlyList<Season> MergeSeriesInfo(
        IReadOnlyList<IReadOnlyDictionary<int, SeriesInfo>> sources)
    {
        var seasons = new SortedDictionary<int, (string Title, SortedDictionary<int, (string Title, List<EpisodeTranslation> Translations)> Episodes)>();
        foreach (var source in sources)
        {
            foreach (var series in source.Values)
            {
                foreach (var season in series.Seasons)
                {
                    if (!seasons.TryGetValue(season.Key, out var bucket))
                    {
                        bucket = (season.Value, new SortedDictionary<int, (string, List<EpisodeTranslation>)>());
                        seasons[season.Key] = bucket;
                    }

                    if (!series.Episodes.TryGetValue(season.Key, out var episodes))
                    {
                        continue;
                    }

                    foreach (var episode in episodes)
                    {
                        if (!bucket.Episodes.TryGetValue(episode.Key, out var episodeBucket))
                        {
                            episodeBucket = (episode.Value, new List<EpisodeTranslation>());
                            bucket.Episodes[episode.Key] = episodeBucket;
                        }

                        episodeBucket.Translations.Add(new EpisodeTranslation(
                            series.TranslatorId,
                            series.TranslatorName,
                            series.IsPremium));
                    }
                }
            }
        }

        return seasons
            .Select(season => new Season(
                season.Key,
                season.Value.Title,
                season.Value.Episodes
                    .Select(episode => new Episode(
                        episode.Key,
                        episode.Value.Title,
                        episode.Value.Translations))
                    .ToList()))
            .ToList();
    }

    private void ApplyMediaPreview(
        string title,
        Uri? imageUrl,
        DeferredImageSource imageSource,
        string category,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        DetailsTitle = TitleFormatter.Normalize(title);
        _detailsImageUrl = imageUrl;
        if (imageUrl is not null)
        {
            SetDetailsImageSource(imageSource.Value, requestVersion, cancellationToken);
        }

        DetailsMeta = category;
        DetailsRating = string.Empty;
        IsSeries = category.Contains("сериал", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildPlaybackSelectionKey() =>
        $"{SelectedTranslation?.Id}:{SelectedSeason?.Value}:{SelectedEpisode?.Value}";

    private void ApplyMediaMetadata(
        CachedMediaMetadata metadata,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        DetailsTitle = TitleFormatter.Reconcile(
            metadata.Title,
            _detailsOpenRequest?.PreviewTitle);
        DetailsOriginalTitle = string.Join(" · ", metadata.OriginalNames);
        DetailsDescription = metadata.Description;
        DetailsTagline = metadata.Tagline ?? string.Empty;
        _detailsImageUrl = Uri.TryCreate(metadata.ImageUrl, UriKind.Absolute, out var imageUrl)
            ? imageUrl
            : null;
        var mediaUrl = Uri.TryCreate(metadata.Url, UriKind.Absolute, out var parsedMediaUrl)
            ? parsedMediaUrl
            : _rezka.Origin;
        if (_detailsImageUrl is not null)
        {
            SetDetailsImageSource(
                _images.LoadAsync(
                    _detailsImageUrl,
                    mediaUrl,
                    ImageDecodeSize.Details),
                requestVersion,
                cancellationToken);
        }
        DetailsMeta = string.Join(
            "  ·  ",
            new[]
            {
                metadata.ReleaseYear?.ToString(),
                metadata.Category,
                metadata.Format == nameof(MediaFormat.Series) ? "Сериал" : "Фильм"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        DetailsRating = metadata.Rating is { } rating
            ? $"★ {rating:0.0}" +
              (metadata.RatingVotes is { } votes ? $"  ·  {votes:N0} оценок" : string.Empty)
            : "Рейтинг не указан";
        IsSeries = metadata.Format == nameof(MediaFormat.Series);
        CanWatchDetails = metadata.IsPlaybackAvailable;

        ReconcileSimpleCollection(
            DetailsGenres,
            metadata.Genres.Select(item => item.Name));
        ReconcileSimpleCollection(
            DetailsCollections,
            metadata.Collections.Select(item => item.Name));
        ReconcileSimpleCollection(
            DetailsRankings,
            metadata.Rankings.Select(item => item.Name));
        ReconcileSimpleCollection(
            DetailsOtherParts,
            metadata.OtherParts.Select(item => item.Name));
        RebuildDetailGroups();

        DetailsFacts.Clear();
        AddDetailFact("Дата выхода", metadata.ReleaseDate?.ToString("dd MMMM yyyy"));
        AddCountriesFact(metadata.Countries);
        AddDetailFact("Жанры", string.Join(", ", metadata.Genres.Select(item => item.Name)));
        AddAgeFact(metadata.AgeRating);
        AddDetailFact(
            "Время",
            metadata.DurationSeconds is { } seconds
                ? FormatDuration(TimeSpan.FromSeconds(seconds))
                : null);

        DetailsDirectors.Clear();
        foreach (var person in metadata.Directors)
        {
            DetailsDirectors.Add(CreatePersonCard(person, mediaUrl));
        }

        DetailsCast.Clear();
        foreach (var person in metadata.Cast.Take(18))
        {
            DetailsCast.Add(CreatePersonCard(person, mediaUrl));
        }

        DetailsExternalRatings.Clear();
        foreach (var ratingItem in metadata.ExternalRatings)
        {
            Uri.TryCreate(ratingItem.Url, UriKind.Absolute, out var ratingUrl);
            DetailsExternalRatings.Add(new ExternalRatingItem(
                ratingItem.Source,
                ratingItem.Value?.ToString("0.0") ?? "—",
                ratingItem.Votes is { } sourceVotes
                    ? $"{sourceVotes:N0} оценок"
                    : string.Empty,
                ratingUrl));
        }

        DetailsSchedule.Clear();
        foreach (var item in metadata.Schedule.Take(16))
        {
            DetailsSchedule.Add(new ScheduleCardItem(
                $"{item.Season} сезон · {item.Episode} серия",
                item.Title ?? item.OriginalTitle ?? "Без названия",
                item.ReleaseDate?.ToString("dd.MM.yyyy") ?? "Дата не указана",
                item.IsAvailable,
                item.IsWatched));
        }

        DetailsRecommendations.Clear();
        foreach (var item in metadata.Recommendations.Take(12))
        {
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var recommendationUrl))
            {
                continue;
            }

            Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var recommendationImage);
            DetailsRecommendations.Add(CreateCard(
                item.Title,
                recommendationUrl,
                recommendationImage,
                item.Category));
        }
    }

    private void SetDetailsImageSource(
        Task<Bitmap?> imageSource,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_detailsPrimaryImageTask, imageSource) ||
            ReferenceEquals(_detailsUpgradeImageTask, imageSource))
        {
            return;
        }

        if (_hasDetailsPrimaryImageSource)
        {
            IsDetailsHeroImageUpgradeReady = false;
            DetailsHeroUpgradeImageSource = null;
            _detailsUpgradeImageTask = imageSource;
            var upgradeVersion = ++_detailsImageUpgradeVersion;
            _ = RevealDetailsHeroImageUpgradeAsync(
                imageSource,
                requestVersion,
                upgradeVersion,
                cancellationToken);
            return;
        }

        _hasDetailsPrimaryImageSource = true;
        IsDetailsHeroBackgroundReady = false;
        IsDetailsHeroImageUpgradeReady = false;
        IsDetailsHeroPrimaryImageVisible = true;
        DetailsImageSource = null;
        DetailsHeroUpgradeImageSource = null;
        _detailsUpgradeImageTask = Task.FromResult<Bitmap?>(null);
        _detailsImageUpgradeVersion++;
        _detailsPrimaryImageTask = imageSource;
        var imageVersion = ++_detailsImageVersion;
        _ = RevealDetailsHeroBackgroundAsync(
            imageSource,
            requestVersion,
            imageVersion,
            cancellationToken);
    }

    private async Task RevealDetailsHeroBackgroundAsync(
        Task<Bitmap?> imageSource,
        int requestVersion,
        int imageVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await imageSource.WaitAsync(cancellationToken);
            if (image is null)
            {
                return;
            }

            while (!IsDetailsVisible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (imageVersion != _detailsImageVersion)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
            }

            if (requestVersion != _detailsRequestVersion ||
                imageVersion != _detailsImageVersion)
            {
                return;
            }

            DetailsImageSource = image;
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            if (requestVersion == _detailsRequestVersion &&
                imageVersion == _detailsImageVersion &&
                IsDetailsVisible &&
                IsDetailsHeroSurfaceVisible)
            {
                IsDetailsHeroBackgroundReady = true;
            }
        }
        catch (OperationCanceledException)
        {
            // The user left this title before its cover became available.
        }
    }

    private async Task RevealDetailsHeroImageUpgradeAsync(
        Task<Bitmap?> imageSource,
        int requestVersion,
        int upgradeVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await imageSource.WaitAsync(cancellationToken);
            if (image is null)
            {
                return;
            }

            while (!IsDetailsVisible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (upgradeVersion != _detailsImageUpgradeVersion)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
            }

            if (requestVersion != _detailsRequestVersion ||
                upgradeVersion != _detailsImageUpgradeVersion)
            {
                return;
            }

            DetailsHeroUpgradeImageSource = image;
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            if (requestVersion == _detailsRequestVersion &&
                upgradeVersion == _detailsImageUpgradeVersion &&
                IsDetailsVisible &&
                IsDetailsHeroSurfaceVisible)
            {
                IsDetailsHeroImageUpgradeReady = true;
                IsDetailsHeroBackgroundReady = true;
                await Task.Delay(TimeSpan.FromMilliseconds(360), cancellationToken);
                if (requestVersion == _detailsRequestVersion &&
                    upgradeVersion == _detailsImageUpgradeVersion &&
                    IsDetailsVisible)
                {
                    IsDetailsHeroPrimaryImageVisible = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The user left this title before the upgraded cover became available.
        }
    }

    private PersonCardItem CreatePersonCard(CachedPerson person, Uri? referer)
    {
        Uri.TryCreate(person.ImageUrl, UriKind.Absolute, out var imageUrl);
        return new PersonCardItem(
            person.Name,
            person.Job,
            _images.Defer(imageUrl, referer, ImageDecodeSize.Avatar));
    }

    private void AddDetailFact(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            DetailsFacts.Add(new DetailFactItem(label, value));
        }
    }

    private void AddCountriesFact(IReadOnlyList<CachedNamedLink> countries)
    {
        if (countries.Count == 0)
        {
            return;
        }

        var countryItems = countries
            .Select((country, index) => CountryFlagAssets.Create(
                country,
                index < countries.Count - 1))
            .ToArray();
        DetailsFacts.Add(new DetailFactItem(
            "Страны",
            string.Join(", ", countryItems.Select(item => item.Name)),
            countryItems));
    }

    private void AddAgeFact(string? ageRating)
    {
        if (string.IsNullOrWhiteSpace(ageRating))
        {
            return;
        }

        var icon = AgeRatingAssets.Get(ageRating);
        DetailsFacts.Add(icon is null
            ? new DetailFactItem("Возраст", ageRating)
            : new DetailFactItem("Возраст", ageRating, AgeIcon: icon));
    }

    private void RebuildDetailGroups()
    {
        var groups = new[]
        {
            (Title: "Коллекции", Items: (IReadOnlyList<string>)DetailsCollections.ToArray()),
            (Title: "Место в подборках", Items: (IReadOnlyList<string>)DetailsRankings.ToArray()),
            (Title: "Другие части", Items: (IReadOnlyList<string>)DetailsOtherParts.ToArray())
        }.Where(group => group.Items.Count > 0).ToArray();

        DetailsGroups.Clear();
        DetailsGroupColumnCount = Math.Max(groups.Length, 1);
        var innerColumnCount = groups.Length switch
        {
            1 => 3,
            2 => 2,
            _ => 1
        };

        foreach (var group in groups)
        {
            DetailsGroups.Add(new DetailGroupCardItem(
                group.Title,
                group.Items,
                innerColumnCount));
        }

        HasDetailGroups = DetailsGroups.Count > 0;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} ч {duration.Minutes} мин"
            : $"{duration.Minutes} мин";

    private static void ReconcileSimpleCollection(
        ObservableCollection<string> target,
        IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }

    [RelayCommand]
    private async Task LoadMoreCommentsAsync()
    {
        if (_media is null || !CanLoadMoreComments)
        {
            return;
        }

        await LoadCommentsPageAsync(
            CommentsPage + 1,
            replace: false,
            _lifetimeCancellation.Token);
    }

    private async Task LoadCommentsPageAsync(
        int page,
        bool replace,
        CancellationToken cancellationToken)
    {
        if (_media is null || IsCommentsLoading)
        {
            return;
        }

        var media = _media;
        IsCommentsLoading = true;
        try
        {
            var result = await Task.Run(
                () => _rezka.GetCommentsAsync(media, page, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_media, media))
            {
                return;
            }

            if (replace)
            {
                DetailsComments.Clear();
                _commentNodeIndex = [];
            }

            var requestVersion = _detailsRequestVersion;
            var newNodes = BuildCommentTree(
                result.Items,
                comment =>
                {
                    var node = CreateCommentNode(comment, media);
                    return (comment.Id, comment.ParentId, node);
                },
                _commentNodeIndex);
            if (requestVersion != _detailsRequestVersion)
            {
                return;
            }

            foreach (var node in newNodes)
            {
                DetailsComments.Add(node);
            }

            foreach (var node in _commentNodeIndex.Values)
            {
                node.NotifyChildrenChanged();
            }

            CommentsPage = result.Page;
            CommentsTotalPages = result.TotalPages;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Комментарии недоступны: {ToUserMessage(exception)}";
        }
        finally
        {
            IsCommentsLoading = false;
        }
    }

    private CommentNodeItem CreateCommentNode(CachedComment comment, Media media)
    {
        Uri.TryCreate(comment.AvatarUrl, UriKind.Absolute, out var avatarUrl);
        return new CommentNodeItem(
            comment.Id,
            comment.ParentId,
            comment.Depth,
            comment.Author,
            comment.DateLabel,
            comment.Text,
            comment.Likes,
            _images.Defer(avatarUrl, media.Url, ImageDecodeSize.Avatar));
    }

    /// <summary>
    /// Rebuilds a parent/child comment tree from the flat, in-nesting-order
    /// list returned by the website, linking replies through
    /// <see cref="CommentNodeItem.ParentId"/>. Existing nodes in
    /// <paramref name="existingIndex"/> are reused across paginated loads so a
    /// reply arriving on a later page attaches to the parent built earlier.
    /// Replies whose parent has not been seen yet are treated as roots rather
    /// than dropped, matching how the flat list degrades when pages are split.
    /// </summary>
    /// <typeparam name="TComment">
    /// Flat source comment type (library <see cref="Comment"/> or cached).
    /// </typeparam>
    /// <param name="factory">
    /// Builds a fresh <see cref="CommentNodeItem"/> and exposes the source
    /// id/parentId for one comment without side effects.
    /// </param>
    /// <param name="existingIndex">
    /// Shared id -> node map, mutated in place to include the new nodes.
    /// </param>
    /// <returns>
    /// Only the roots produced by this batch (parent missing or already known).
    /// </returns>
    internal static IReadOnlyList<CommentNodeItem> BuildCommentTree<TComment>(
        IReadOnlyList<TComment> comments,
        Func<TComment, (long Id, long? ParentId, CommentNodeItem Node)> factory,
        Dictionary<long, CommentNodeItem> existingIndex)
    {
        var built = new List<(long Id, long? ParentId, CommentNodeItem Node)>(comments.Count);
        var batchNodes = new Dictionary<long, CommentNodeItem>();
        foreach (var comment in comments)
        {
            var entry = factory(comment);
            built.Add(entry);
            batchNodes[entry.Id] = entry.Node;
        }

        var roots = new List<CommentNodeItem>();
        foreach (var (id, parentId, node) in built)
        {
            if (parentId is { } parent &&
                batchNodes.TryGetValue(parent, out var batchParent))
            {
                batchParent.Children.Add(node);
            }
            else if (parentId is { } existingParent &&
                     existingIndex.TryGetValue(existingParent, out var knownParent))
            {
                knownParent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            existingIndex[id] = node;
        }

        return roots;
    }

    private async Task LoadEpisodesForSeasonAsync(
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        if (_media is null)
        {
            return;
        }

        try
        {
            var media = _media;
            var seasons = await LoadSeriesInfoResilientAsync(
                media,
                cancellationToken);
            var season = seasons?.FirstOrDefault(item => item.Number == seasonNumber);
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ToUserMessage(exception);
        }
    }

    private MediaCardItem CreateCard(
        string title,
        Uri url,
        Uri? imageUrl,
        string category)
    {
        var displayTitle = TitleFormatter.Normalize(title);
        var imageSource = _images.Defer(imageUrl, url, ImageDecodeSize.Card);
        return new MediaCardItem(
            displayTitle,
            url,
            imageSource,
            category,
            () => OpenMediaAsync(
                url,
                displayTitle,
                imageUrl,
                imageSource,
                category));
    }

    private async Task RebuildRecentAsync(CancellationToken cancellationToken = default)
    {
        Recent.Clear();
        foreach (var item in (await _rezka.GetRecentAsync(cancellationToken)).Take(12))
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

        var latestEntry = snapshot.ContinueWatching.FirstOrDefault();
        var latestCard = ContinueWatching.FirstOrDefault();
        ContinueWatchingHero = latestEntry is not null && latestCard is not null
            ? new ContinueWatchingHeroItem(
                latestCard,
                _images.Defer(latestEntry.ImageUrl, latestEntry.Url, ImageDecodeSize.Hero),
                BuildPlaybackPosition(latestEntry),
                BuildLastViewedLabel(
                    latestEntry.Date,
                    latestEntry.DateLabel,
                    DateOnly.FromDateTime(DateTime.Now)),
                latestEntry.Details?.Trim() ?? string.Empty)
            : null;

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
        var displayTitle = TitleFormatter.Normalize(title);
        if (existing.TryGetValue(url.AbsoluteUri, out var card) &&
            string.Equals(card.Title, displayTitle, StringComparison.Ordinal) &&
            string.Equals(card.Category, category, StringComparison.Ordinal))
        {
            return card;
        }

        return CreateCard(displayTitle, url, imageUrl, category);
    }

    private static string BuildPlaybackPosition(ContinueWatchingEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PlaybackInformation))
        {
            return entry.PlaybackInformation.Trim();
        }

        if (entry.Season is int season && entry.Episode is int episode)
        {
            return $"{season} сезон · {episode} серия";
        }

        if (entry.Episode is int standaloneEpisode)
        {
            return $"{standaloneEpisode} серия";
        }

        if (!string.IsNullOrWhiteSpace(entry.Translator))
        {
            return entry.Translator.Trim();
        }

        return entry.IsWatched
            ? "Просмотр завершён"
            : "Последняя сохранённая позиция";
    }

    internal static string BuildLastViewedLabel(
        DateOnly? viewedOn,
        string? sourceLabel,
        DateOnly today)
    {
        var normalizedLabel = sourceLabel?.Trim() ?? string.Empty;
        if (normalizedLabel.Contains("сегодня", StringComparison.OrdinalIgnoreCase))
        {
            viewedOn = today;
        }
        else if (normalizedLabel.Contains("позавчера", StringComparison.OrdinalIgnoreCase))
        {
            viewedOn = today.AddDays(-2);
        }
        else if (normalizedLabel.Contains("вчера", StringComparison.OrdinalIgnoreCase))
        {
            viewedOn = today.AddDays(-1);
        }
        else if (viewedOn is null)
        {
            var formats = new[] { "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yyyy", "yyyy-MM-dd" };
            if (DateOnly.TryParseExact(
                    normalizedLabel,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                viewedOn = parsedDate;
            }
        }

        if (viewedOn is not { } date)
        {
            return string.IsNullOrWhiteSpace(normalizedLabel)
                ? "Смотрели недавно"
                : $"Смотрели {normalizedLabel}";
        }

        if (date == today)
        {
            return "Смотрели сегодня";
        }

        if (date == today.AddDays(-1))
        {
            return "Смотрели вчера";
        }

        if (date == today.AddDays(-2))
        {
            return "Смотрели позавчера";
        }

        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var startOfWeek = today.AddDays(-daysSinceMonday);
        if (date >= startOfWeek && date <= today)
        {
            return "Смотрели на этой неделе";
        }

        if (date.Year == today.Year && date.Month == today.Month)
        {
            return "Смотрели в этом месяце";
        }

        if (date.Year == today.Year - 1)
        {
            return "Смотрели в прошлом году";
        }

        return $"Смотрели {date:dd.MM.yyyy}";
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
        ProfileImageSource = _images.LoadAsync(
            profile?.AvatarUrl,
            _rezka.Origin,
            ImageDecodeSize.Avatar);
        AccountLabel = IsPremium ? "Premium" : ProfileName;
    }

    private async Task RunBusyAsync(
        Func<CancellationToken, Task> action,
        bool showActivity = true,
        CancellationToken cancellationToken = default)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var operationCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _operationCancellation = operationCancellation;
        IsBusy = showActivity;
        try
        {
            await action(operationCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation superseded the operation; the destination should stay quiet.
        }
        catch (OperationCanceledException)
        {
            if (showActivity)
            {
                StatusMessage = "Операция отменена";
            }
        }
        catch (LoginRequiredException)
        {
            ShowAuthenticationRequired(
                "Сессия истекла. Войдите в аккаунт ещё раз");
        }
        catch (Exception exception)
        {
            if (showActivity)
            {
                StatusMessage = ToUserMessage(exception);
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, operationCancellation))
            {
                _operationCancellation = null;
                if (showActivity)
                {
                    IsBusy = false;
                }
            }

            operationCancellation.Dispose();
        }
    }

    private async Task NavigateAsync(
        Page page,
        CancellationToken cancellationToken = default,
        bool animateEntrance = true)
    {
        if (_disposed)
        {
            return;
        }

        if (page is not (Page.Details or Page.Player))
        {
            CancelDetailsLoad(invalidateRequest: true);
        }

        if (page == _currentPage && IsPageVisible(page))
        {
            if (!IsPageLeaving(page))
            {
                return;
            }

            _pageNavigationCancellation?.Cancel();
            _pageTransitionVersion++;
            RestorePageAfterCancelledExit(page);
            _logger.Debug(
                "Cancelled pending exit and restored {Page}",
                page);
            return;
        }

        _pageNavigationCancellation?.Cancel();
        _pageNavigationCancellation?.Dispose();
        var navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        _pageNavigationCancellation = navigationCancellation;
        var transitionVersion = ++_pageTransitionVersion;
        var previousPage = _currentPage;
        _logger.Debug(
            "Navigating from {PreviousPage} to {NextPage}; animated={AnimateEntrance}",
            previousPage,
            page,
            animateEntrance);

        IsProfilePopupOpen = false;
        IsCategoryMenuOpen = false;
        if (page != Page.Library)
        {
            ActiveCategory = null;
        }

        try
        {
            await PlayPageExitAsync(previousPage, navigationCancellation.Token);
            navigationCancellation.Token.ThrowIfCancellationRequested();
            if (transitionVersion != _pageTransitionVersion)
            {
                return;
            }

            if (page == Page.Home)
            {
                IsHomeLeaving = false;
                IsHomeEntering = animateEntrance;
            }
            else if (page == Page.Details)
            {
                IsDetailsLeaving = false;
                IsDetailsEntering = animateEntrance;
                IsDetailsHeroSurfaceVisible = false;
            }

            ShowPage(page);
            if (page == Page.Details)
            {
                _ = RevealDetailsHeroLayersAsync(
                    transitionVersion,
                    _detailsRequestVersion);
            }

            if (animateEntrance && page is Page.Home or Page.Details)
            {
                _ = CompletePageEntranceAsync(
                    page,
                    transitionVersion,
                    _detailsRequestVersion);
            }
        }
        catch (OperationCanceledException)
        {
            if (transitionVersion == _pageTransitionVersion)
            {
                RestorePageAfterCancelledExit(previousPage);
                _logger.Debug(
                    "Restored {Page} after its page transition was cancelled",
                    previousPage);
            }
        }
        finally
        {
            if (ReferenceEquals(_pageNavigationCancellation, navigationCancellation))
            {
                _pageNavigationCancellation = null;
            }

            navigationCancellation.Dispose();
        }
    }

    private async Task PlayPageExitAsync(Page page, CancellationToken cancellationToken)
    {
        if (page == Page.Home && IsHomeVisible)
        {
            IsHomeEntering = false;
            IsHomeLeaving = true;
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
        else if (page == Page.Details && IsDetailsVisible)
        {
            IsDetailsEntering = false;
            IsDetailsLeaving = true;
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
    }

    private async Task CompletePageEntranceAsync(
        Page page,
        int transitionVersion,
        int detailsRequestVersion)
    {
        var duration = page == Page.Home
            ? HomeEntranceAnimationMilliseconds
            : DetailsEntranceAnimationMilliseconds;
        await Task.Delay(
            TimeSpan.FromMilliseconds(duration),
            _lifetimeCancellation.Token);
        if (_disposed ||
            transitionVersion != _pageTransitionVersion ||
            !IsPageVisible(page) ||
            page == Page.Details &&
            (detailsRequestVersion != _detailsRequestVersion || IsDetailsLeaving))
        {
            return;
        }

        if (page == Page.Home)
        {
            IsHomeEntering = false;
        }
        else
        {
            IsDetailsEntering = false;
        }

        _logger.Debug(
            "Completed transient entrance state for {Page}; transition={TransitionVersion}, details request={DetailsRequestVersion}",
            page,
            transitionVersion,
            detailsRequestVersion);
    }

    private async Task RevealDetailsHeroLayersAsync(
        int transitionVersion,
        int requestVersion)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(20),
                _lifetimeCancellation.Token);
            if (transitionVersion != _pageTransitionVersion ||
                requestVersion != _detailsRequestVersion ||
                !IsDetailsVisible ||
                IsDetailsLeaving)
            {
                return;
            }

            IsDetailsHeroSurfaceVisible = true;
            IsDetailsHeroContentVisible = true;
            if (UseDetailedHeroEntrance)
            {
                _ = RevealDetailsHeroMetadataAsync(
                    transitionVersion,
                    requestVersion);
            }
            else
            {
                IsDetailsHeroMetadataReady = true;
                IsDetailsHeroMetadataVisible = true;
            }
            _ = RevealDetailsHeroBackgroundAsync(
                _detailsPrimaryImageTask,
                requestVersion,
                _detailsImageVersion,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The application closed before the next details frame.
        }
    }

    private bool IsPageLeaving(Page page) => page switch
    {
        Page.Home => IsHomeLeaving,
        Page.Details => IsDetailsLeaving,
        _ => false
    };

    private void RestorePageAfterCancelledExit(Page page)
    {
        if (page == Page.Home && IsHomeVisible)
        {
            IsHomeLeaving = false;
            IsHomeEntering = false;
        }
        else if (page == Page.Details && IsDetailsVisible)
        {
            IsDetailsLeaving = false;
            IsDetailsEntering = false;
        }
    }

    private async Task RevealDetailsHeroMetadataAsync(
        int transitionVersion,
        int requestVersion)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(DetailsHeroMetadataMinimumVisibleMilliseconds));
        while (!IsDetailsContentReady)
        {
            if (_disposed ||
                transitionVersion != _pageTransitionVersion ||
                requestVersion != _detailsRequestVersion ||
                !IsDetailsVisible ||
                IsDetailsLeaving ||
                IsDetailsContentLoadFailed)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(40));
        }

        if (!_disposed &&
            transitionVersion == _pageTransitionVersion &&
            requestVersion == _detailsRequestVersion &&
            IsDetailsVisible &&
            !IsDetailsLeaving)
        {
            IsDetailsHeroMetadataReady = true;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(DetailsHeroHeadingTransitionMilliseconds));
        if (!_disposed &&
            transitionVersion == _pageTransitionVersion &&
            requestVersion == _detailsRequestVersion &&
            IsDetailsVisible &&
            !IsDetailsLeaving &&
            IsDetailsContentReady)
        {
            IsDetailsHeroMetadataVisible = true;
        }
    }

    internal static double CalculateDetailsTitleFontSize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return DetailsHeroTitleMaximumFontSize;
        }

        var estimatedGlyphWidth = title.Sum(character => character switch
        {
            _ when char.IsWhiteSpace(character) => 0.3,
            _ when char.IsUpper(character) => 0.66,
            _ when char.IsDigit(character) => 0.56,
            _ when char.IsPunctuation(character) => 0.34,
            _ => 0.55
        });
        var fittedFontSize = DetailsHeroTitleWidth * DetailsHeroTitleTargetLines /
            Math.Max(estimatedGlyphWidth, 1);

        return Math.Round(
            Math.Clamp(
                fittedFontSize,
                DetailsHeroTitleMinimumFontSize,
                DetailsHeroTitleMaximumFontSize),
            1);
    }

    private bool IsPageVisible(Page page) => page switch
    {
        Page.Home => IsHomeVisible,
        Page.Library => IsLibraryVisible,
        Page.Settings => IsSettingsVisible,
        Page.Details => IsDetailsVisible,
        Page.Player => IsPlayerVisible,
        _ => false
    };

    private void ShowPage(Page page)
    {
        _currentPage = page;
        IsHomeVisible = page == Page.Home;
        IsLibraryVisible = page == Page.Library;
        IsSettingsVisible = page == Page.Settings;
        IsDetailsVisible = page == Page.Details;
        IsPlayerVisible = page == Page.Player;

        if (page != Page.Home)
        {
            IsHomeLeaving = false;
        }

        if (page != Page.Details)
        {
            IsDetailsLeaving = false;
        }
    }

    private void CancelDetailsLoad(bool invalidateRequest = false)
    {
        if (invalidateRequest)
        {
            _detailsRequestVersion++;
        }

        var cancellation = _detailsLoadCancellation;
        _detailsLoadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
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

        var sessionMirror = FindRestorableSessionMirror(_settings, _auth, MirrorStatuses);
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
        AuthState auth,
        IEnumerable<MirrorStatusItem> mirrors)
    {
        if (!settings.RememberSession
            || auth.Cookies.Count == 0
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
        PremiumRequiredException premium => premium.Feature switch
        {
            PremiumFeature.Content =>
                "Сайт пометил тайтл как Premium. Часть переводов может быть недоступна — попробуйте другое зеркало или переоткройте тайтл",
            PremiumFeature.Translation =>
                premium.Name is null
                    ? "Выбранный перевод требует Premium"
                    : $"Перевод «{premium.Name}» требует Premium",
            _ => "Выбранное качество требует Premium",
        },
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
        CancelDetailsLoad();
        _pageNavigationCancellation?.Cancel();
        _pageNavigationCancellation?.Dispose();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private sealed record DetailsOpenRequest(
        Uri Url,
        string PreviewTitle,
        Uri? PreviewImageUrl,
        DeferredImageSource PreviewImageSource,
        string PreviewCategory);

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
