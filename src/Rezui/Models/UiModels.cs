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
