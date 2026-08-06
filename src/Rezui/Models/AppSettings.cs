namespace Rezui.Models;

public sealed class AppSettings
{
    public string Origin { get; set; } = RezkaMirrors.Primary;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public bool RememberSession { get; set; } = true;

    public List<string> CustomMirrors { get; set; } = [];

    public List<string> PinnedMediaUrls { get; set; } = [];

    public GraphicsAdapterPreference GraphicsAdapter { get; set; } =
        GraphicsAdapterPreference.Auto;
}

public sealed class AuthState
{
    public Dictionary<string, string> Cookies { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed record RecentMedia(
    string Title,
    string Url,
    string ImageUrl,
    string Category,
    DateTimeOffset OpenedAt);

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public enum GraphicsAdapterPreference
{
    Auto,
    Discrete,
    Integrated
}
