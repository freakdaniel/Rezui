using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Platform;

namespace Rezui.Services;

internal static partial class AgeRatingAssets
{
    private static readonly HashSet<string> SupportedAges = ["0", "6", "12", "16", "18"];

    private static readonly ConcurrentDictionary<string, Lazy<StreamGeometry?>> Icons =
        new(StringComparer.Ordinal);

    public static StreamGeometry? Get(string? ageRating)
    {
        var age = ExtractAgeCode(ageRating);
        if (age is null)
        {
            return null;
        }

        return Icons.GetOrAdd(
                age,
                static value => new Lazy<StreamGeometry?>(
                    () => Load(value),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    internal static string? ExtractAgeCode(string? ageRating)
    {
        if (string.IsNullOrWhiteSpace(ageRating))
        {
            return null;
        }

        var match = AgePattern().Match(ageRating);
        return match.Success && SupportedAges.Contains(match.Groups[1].Value)
            ? match.Groups[1].Value
            : null;
    }

    private static StreamGeometry? Load(string age)
    {
        try
        {
            var uri = new Uri($"avares://Rezui/Assets/Ages/{age}.svg");
            using var stream = AssetLoader.Open(uri);
            var document = XDocument.Load(stream);
            var pathData = document
                .Descendants()
                .Where(element => element.Name.LocalName == "path")
                .Select(element => element.Attribute("d")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var combinedPath = string.Join(" ", pathData);
            return string.IsNullOrWhiteSpace(combinedPath)
                ? null
                : StreamGeometry.Parse(combinedPath);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
                ArgumentException or
                InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<!\d)(0|6|12|16|18)\s*\+?", RegexOptions.CultureInvariant)]
    private static partial Regex AgePattern();
}
