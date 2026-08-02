using Avalonia;
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

public sealed record CountryFlagItem(string Name, Bitmap? ImageSource)
{
    public bool HasImage => ImageSource is not null;
}

public sealed record PersonCardItem(
    string Name,
    string Job,
    Task<Bitmap?> ImageSource);

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

public sealed record CommentCardItem(
    long Id,
    string Author,
    string DateLabel,
    string Text,
    int Likes,
    Thickness Indent,
    Task<Bitmap?> AvatarSource);

public sealed class ContinueWatchingHeroItem
{
    public ContinueWatchingHeroItem(
        MediaCardItem media,
        string playbackPosition,
        string lastViewedLabel,
        string details)
    {
        Title = media.Title;
        ImageSource = media.ImageSource;
        Category = media.Category;
        PlaybackPosition = playbackPosition;
        LastViewedLabel = lastViewedLabel;
        Details = details;
        OpenCommand = media.OpenCommand;
    }

    public string Title { get; }

    public Task<Bitmap?> ImageSource { get; }

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
        Task<Bitmap?> imageSource,
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

    public Task<Bitmap?> ImageSource { get; }

    public string Category { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
