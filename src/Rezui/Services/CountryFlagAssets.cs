using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Rezui.Models;

namespace Rezui.Services;

internal static class CountryFlagAssets
{
    private static readonly IReadOnlyDictionary<string, string> CountryAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UK"] = "GB",
            ["США"] = "US",
            ["Великобритания"] = "GB",
            ["Англия"] = "GB",
            ["Россия"] = "RU",
            ["Украина"] = "UA",
            ["Беларусь"] = "BY",
            ["Япония"] = "JP",
            ["Китай"] = "CN",
            ["Южная Корея"] = "KR",
            ["Северная Корея"] = "KP",
            ["ОАЭ"] = "AE",
            ["Гонконг"] = "HK",
            ["Тайвань"] = "TW",
            ["Чехия"] = "CZ",
            ["Вьетнам"] = "VN"
        };

    private static readonly IReadOnlyDictionary<string, string> Alpha3ToAlpha2 =
        BuildAlpha3Lookup();

    private static readonly ConcurrentDictionary<string, Lazy<Bitmap?>> Flags =
        new(StringComparer.OrdinalIgnoreCase);

    public static CountryFlagItem Create(CachedNamedLink country)
    {
        var countryCode = ResolveCountryCode(country.Name, country.Url);
        var image = countryCode is null
            ? null
            : Flags.GetOrAdd(
                    countryCode,
                    static code => new Lazy<Bitmap?>(
                        () => Load(code),
                        LazyThreadSafetyMode.ExecutionAndPublication))
                .Value;

        return new CountryFlagItem(country.Name, image);
    }

    internal static string? ResolveCountryCode(string name, string? url)
    {
        var urlCode = TryReadUrlCode(url);
        if (urlCode is not null)
        {
            return urlCode;
        }

        return CountryAliases.TryGetValue(name.Trim(), out var alias)
            ? alias
            : null;
    }

    private static string? TryReadUrlCode(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Segments.Length == 0)
        {
            return null;
        }

        var segment = Uri.UnescapeDataString(uri.Segments[^1]).Trim('/').Trim();
        if (CountryAliases.TryGetValue(segment, out var alias))
        {
            return alias;
        }

        if (segment.Length == 2 && segment.All(char.IsLetter))
        {
            return segment.ToUpperInvariant();
        }

        return Alpha3ToAlpha2.TryGetValue(segment, out var countryCode)
            ? countryCode
            : null;
    }

    private static IReadOnlyDictionary<string, string> BuildAlpha3Lookup()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                result.TryAdd(
                    region.ThreeLetterISORegionName,
                    region.TwoLetterISORegionName);
            }
            catch (ArgumentException)
            {
                // A synthetic culture without a region cannot identify a flag.
            }
        }

        return result;
    }

    private static Bitmap? Load(string countryCode)
    {
        try
        {
            var uri = new Uri(
                $"avares://Rezui/Assets/CountryFlags/{countryCode.ToLowerInvariant()}.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }
}
