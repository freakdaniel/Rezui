namespace Rezui.Models;

public static class RezkaMirrors
{
    public const string Primary = "https://hdrezka.fi";

    public static IReadOnlyList<string> Defaults { get; } = Array.AsReadOnly(
        [
            Primary,
            "https://rezka.fi",
            "https://hdrezka.cm",
            "https://standby-rezka.tv/"
        ]);

    public static bool IsDefault(string origin) => Defaults.Any(
        mirror => string.Equals(mirror, origin, StringComparison.OrdinalIgnoreCase));
}
