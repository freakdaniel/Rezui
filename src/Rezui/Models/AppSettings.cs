namespace Rezui.Models;

public sealed class AppSettings
{
    public string Origin { get; set; } = string.Empty;

    public bool RememberSession { get; set; } = true;

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

