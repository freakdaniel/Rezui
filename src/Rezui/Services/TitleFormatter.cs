namespace Rezui.Services;

internal static class TitleFormatter
{
    private const string AlternativeTitleSeparator = " / ";

    public static string Normalize(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        var separatorIndex = value.IndexOf(
            AlternativeTitleSeparator,
            StringComparison.Ordinal);

        return separatorIndex > 0
            ? value[..separatorIndex].TrimEnd()
            : value;
    }

    public static string Reconcile(string? parsedTitle, string? previewTitle)
    {
        var parsed = Normalize(parsedTitle);
        var preview = Normalize(previewTitle);
        if (string.IsNullOrEmpty(preview))
        {
            return parsed;
        }

        if (string.IsNullOrEmpty(parsed))
        {
            return preview;
        }

        // HDRezka.NET 0.0.6 splits page titles by every slash. Preserve a
        // catalog title when the parsed value is only the prefix of an
        // intentional title such as ".хак//Корни" or "AC/DC".
        return preview.StartsWith($"{parsed}/", StringComparison.OrdinalIgnoreCase)
            ? preview
            : parsed;
    }
}
