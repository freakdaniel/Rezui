namespace Rezui.Models;

public sealed class AppSettings
{
    public string Origin { get; set; } = RezkaMirrors.Primary;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public bool RememberSession { get; set; } = true;

    public List<string> CustomMirrors { get; set; } = [];

    public Dictionary<string, string> AuthenticationCookies { get; set; } =
        new(StringComparer.Ordinal);

    public List<RecentMedia> Recent { get; set; } = [];
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

