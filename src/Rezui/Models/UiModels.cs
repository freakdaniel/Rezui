using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Rezui.Models;

public sealed record ChoiceItem(int Value, string Title);

public sealed record TranslationItem(int Id, string Name, bool IsPremium)
{
    public string DisplayName => IsPremium ? $"{Name}  ·  Premium" : Name;
}

public sealed record QualityItem(
    string Name,
    bool RequiresPremium,
    bool IsAvailable,
    IReadOnlyList<Uri> Urls)
{
    public string DisplayName =>
        RequiresPremium && !IsAvailable ? $"{Name}  ·  нужен Premium" : Name;
}

public sealed record SubtitleItem(string Title, Uri? Url);

public sealed partial class MirrorStatusItem : ObservableObject
{
    public MirrorStatusItem(
        string origin,
        string displayName,
        long? latencyMilliseconds,
        bool isAvailable,
        bool isCustom,
        bool isSelected)
    {
        Origin = origin;
        DisplayName = displayName;
        LatencyMilliseconds = latencyMilliseconds;
        IsAvailable = isAvailable;
        IsCustom = isCustom;
        _isSelected = isSelected;
    }

    public string Origin { get; }

    public string DisplayName { get; }

    public long? LatencyMilliseconds { get; }

    public bool IsAvailable { get; }

    public bool IsCustom { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string StatusText => IsAvailable
        ? $"{LatencyMilliseconds} мс"
        : "Недоступно";
}

public sealed record LibraryFolderItem(
    string Name,
    int ItemCount,
    IReadOnlyList<MediaCardItem> Items);

public sealed record DetailFactItem(
    string Label,
    string Value,
    IReadOnlyList<CountryFlagItem>? Countries = null,
    StreamGeometry? AgeIcon = null)
{
    public bool HasCountries => Countries is { Count: > 0 };

    public bool HasAgeIcon => AgeIcon is not null;

    public bool HasPlainValue => !HasCountries && !HasAgeIcon;
}

public sealed record CountryFlagItem(
    string Name,
    Bitmap? ImageSource,
    bool HasTrailingSeparator = false)
{
    public bool HasImage => ImageSource is not null;
}

public sealed class DeferredImageSource
{
    private readonly Lazy<Task<Bitmap?>> _image;

    public DeferredImageSource(Func<Task<Bitmap?>> load) =>
        _image = new Lazy<Task<Bitmap?>>(load, LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<Bitmap?> Value => _image.Value;
}

public sealed record PersonCardItem(
    string Name,
    string Job,
    DeferredImageSource ImageSource);

public sealed record ExternalRatingItem(
    string Source,
    string Value,
    string Votes,
    Uri? Url)
{
    public bool IsImdb =>
        Source.Contains("imdb", StringComparison.OrdinalIgnoreCase);

    public bool IsKinopoisk =>
        Source.Contains("кинопоиск", StringComparison.OrdinalIgnoreCase) ||
        Source.Contains("kinopoisk", StringComparison.OrdinalIgnoreCase);

    public bool IsWorldArt =>
        Source.Contains("world art", StringComparison.OrdinalIgnoreCase) ||
        Source.Contains("world-art", StringComparison.OrdinalIgnoreCase) ||
        Source.Contains("worldart", StringComparison.OrdinalIgnoreCase);

    public bool IsOther => !IsImdb && !IsKinopoisk && !IsWorldArt;

    public bool HasUrl => Url is not null;
}

public sealed record ScheduleCardItem(
    string EpisodeLabel,
    string Title,
    string DateLabel,
    bool IsAvailable,
    bool IsWatched);

public sealed class DetailGroupCardItem
{
    public DetailGroupCardItem(
        string title,
        IReadOnlyList<string> items,
        int maximumColumns)
    {
        ArgumentOutOfRangeException.ThrowIfZero(items.Count);
        Title = title;
        var columnCount = Math.Min(Math.Max(maximumColumns, 1), items.Count);
        var minimumColumnSize = items.Count / columnCount;
        var widerColumnCount = items.Count % columnCount;
        var offset = 0;
        var columns = new List<DetailGroupColumnItem>(columnCount);
        for (var index = 0; index < columnCount; index++)
        {
            var size = minimumColumnSize + (index < widerColumnCount ? 1 : 0);
            columns.Add(new DetailGroupColumnItem(items.Skip(offset).Take(size).ToArray()));
            offset += size;
        }

        Columns = columns;
    }

    public string Title { get; }

    public IReadOnlyList<DetailGroupColumnItem> Columns { get; }

    public int ColumnCount => Columns.Count;
}

public sealed record DetailGroupColumnItem(IReadOnlyList<string> Items);

public sealed partial class CommentNodeItem : ObservableObject
{
    public const int AlwaysVisibleReplies = 3;

    public CommentNodeItem(
        long id,
        long? parentId,
        int depth,
        string author,
        string dateLabel,
        string text,
        int likes,
        DeferredImageSource avatarSource)
    {
        Id = id;
        ParentId = parentId;
        Depth = depth;
        Author = author;
        DateLabel = dateLabel;
        Text = text;
        Likes = likes;
        AvatarSource = avatarSource;
    }

    public long Id { get; }

    public long? ParentId { get; }

    public int Depth { get; }

    public string Author { get; }

    public string DateLabel { get; }

    public string Text { get; }

    public int Likes { get; }

    public DeferredImageSource AvatarSource { get; }

    public ObservableCollection<CommentNodeItem> Children { get; } = [];

    public bool HasReplies => Children.Count > 0;

    /// <summary>
    /// Whether more replies exist than the <see cref="AlwaysVisibleReplies"/>
    /// always-shown preview. When <c>true</c>, the reply list shows the first
    /// <see cref="AlwaysVisibleReplies"/> children always and the rest behind a
    /// "show more replies" button; when <c>false</c> the whole thread toggles.
    /// </summary>
    public bool HasOverflow => Children.Count > AlwaysVisibleReplies;

    /// <summary>
    /// Whether the whole-branch toggle button (for short threads with ≤3
    /// replies) should be rendered. Separate from <see cref="HasOverflow"/>
    /// to avoid showing a toggle on comments without any replies.
    /// </summary>
    public bool CanToggleBranch => HasReplies && !HasOverflow;

    public GridLength BranchRowHeight =>
        HasReplies ? GridLength.Auto : new GridLength(0);

    public int AvatarRowSpan => HasReplies ? 3 : 1;

    /// <summary>
    /// All replies currently shown by the template. Keeping them in a single
    /// list prevents empty reply panels from adding spacing between nested
    /// toggle buttons.
    /// </summary>
    public IReadOnlyList<CommentNodeItem> VisibleReplies =>
        HasOverflow
            ? (IsExpanded ? Children : Children.Take(AlwaysVisibleReplies).ToList())
            : (IsExpanded ? Children : Array.Empty<CommentNodeItem>());

    /// <summary>
    /// Replies rendered directly under this comment. For short threads (≤3
    /// replies) this is the full set, hidden when collapsed. For long threads
    /// (>3) the first three are always shown and the remainder lives in
    /// <see cref="OverflowReplies"/>.
    /// </summary>
    public IReadOnlyList<CommentNodeItem> RenderedReplies =>
        HasOverflow
            ? Children.Take(AlwaysVisibleReplies).ToList()
            : (IsExpanded ? Children : Array.Empty<CommentNodeItem>());

    /// <summary>
    /// Replies beyond the always-shown preview of a long thread. Empty unless
    /// <see cref="HasOverflow"/> and the thread is expanded.
    /// </summary>
    public IReadOnlyList<CommentNodeItem> OverflowReplies =>
        HasOverflow && IsExpanded
            ? Children.Skip(AlwaysVisibleReplies).ToList()
            : Array.Empty<CommentNodeItem>();

    /// <summary>
    /// Label for the button that collapses the whole reply thread. Only used
    /// for short threads (≤3 replies); long threads are collapsed piecewise.
    /// </summary>
    public string CollapseLabel =>
        IsExpanded ? "Свернуть ответы" : $"Показать ответы ({Children.Count})";

    /// <summary>
    /// Label for the "show more replies" button on long threads (>3 replies).
    /// </summary>
    public string OverflowLabel =>
        IsExpanded
            ? "Свернуть"
            : $"Другие ответы ({Children.Count - AlwaysVisibleReplies})";

    public string CollapseIcon => IsExpanded ? "expand_less" : "expand_more";

    [ObservableProperty]
    private bool _isExpanded = true;

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleReplies));
        OnPropertyChanged(nameof(RenderedReplies));
        OnPropertyChanged(nameof(OverflowReplies));
        OnPropertyChanged(nameof(CollapseLabel));
        OnPropertyChanged(nameof(OverflowLabel));
        OnPropertyChanged(nameof(CollapseIcon));
    }

    internal void NotifyChildrenChanged()
    {
        OnPropertyChanged(nameof(HasReplies));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(CanToggleBranch));
        OnPropertyChanged(nameof(BranchRowHeight));
        OnPropertyChanged(nameof(AvatarRowSpan));
        OnPropertyChanged(nameof(VisibleReplies));
        OnPropertyChanged(nameof(RenderedReplies));
        OnPropertyChanged(nameof(OverflowReplies));
        OnPropertyChanged(nameof(CollapseLabel));
        OnPropertyChanged(nameof(OverflowLabel));
    }
}

public sealed class ContinueWatchingHeroItem
{
    public ContinueWatchingHeroItem(
        MediaCardItem media,
        DeferredImageSource backgroundImageSource,
        string playbackPosition,
        string lastViewedLabel,
        string details)
    {
        Title = media.Title;
        ImageSource = media.ImageSource;
        BackgroundImageSource = backgroundImageSource;
        Category = media.Category;
        PlaybackPosition = playbackPosition;
        LastViewedLabel = lastViewedLabel;
        Details = details;
        OpenCommand = media.OpenCommand;
    }

    public string Title { get; }

    public DeferredImageSource ImageSource { get; }

    public DeferredImageSource BackgroundImageSource { get; }

    public string Category { get; }

    public string PlaybackPosition { get; }

    public string LastViewedLabel { get; }

    public string Details { get; }

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public IAsyncRelayCommand OpenCommand { get; }
}

public sealed record CategoryMenuItem(string Title, string Descriptor);

public sealed record CategoryMenuColumn(IReadOnlyList<CategoryMenuItem> Items);

public sealed record CategoryMenuDefinition(
    string Title,
    string AllLabel,
    string AllDescriptor,
    string NewDescriptor,
    IReadOnlyList<CategoryMenuColumn> Columns);

public sealed class MediaCardItem
{
    public MediaCardItem(
        string title,
        Uri url,
        DeferredImageSource imageSource,
        string category,
        Func<Task> open)
    {
        Title = title;
        Url = url;
        ImageSource = imageSource;
        Category = category;
        OpenCommand = new AsyncRelayCommand(open);
    }

    public string Title { get; }

    public Uri Url { get; }

    public DeferredImageSource ImageSource { get; }

    public string Category { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
