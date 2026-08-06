using System.Collections.ObjectModel;
using Rezui.Models;

namespace Rezui.Services;

/// <summary>
/// Transforms a <see cref="CachedMediaMetadata"/> snapshot into the concrete
/// card collections shown on the title details page (facts, directors, cast,
/// external ratings, schedule, recommendations, and the detail groups).
/// Extracted from MainWindowViewModel so the 3.7k-line view-model no longer
/// owns this pure data-mapping concern.
/// </summary>
/// <remarks>
/// The mapper is stateless: it only builds snapshots, leaving the view-model
/// responsible for assigning them to its observable collections (which bind to
/// the UI). Image deferral is delegated to <see cref="ImageCacheService"/>.
/// </remarks>
public static class DetailsMetadataMapper
{
    /// <summary>
    /// Builds the fact rows: release date, countries (with flag assets), genres,
    /// age rating (with icon), and runtime.
    /// </summary>
    public static IReadOnlyList<DetailFactItem> BuildFacts(CachedMediaMetadata metadata)
    {
        var facts = new List<DetailFactItem>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                facts.Add(new DetailFactItem(label, value));
            }
        }

        Add("Дата выхода", metadata.ReleaseDate?.ToString("dd MMMM yyyy"));
        AddCountriesFact(facts, metadata.Countries);
        Add("Жанры", string.Join(", ", metadata.Genres.Select(item => item.Name)));
        AddAgeFact(facts, metadata.AgeRating);
        Add(
            "Время",
            metadata.DurationSeconds is { } seconds
                ? FormatDuration(TimeSpan.FromSeconds(seconds))
                : null);
        return facts;
    }

    private static void AddCountriesFact(
        List<DetailFactItem> facts,
        IReadOnlyList<CachedNamedLink> countries)
    {
        if (countries.Count == 0)
        {
            return;
        }

        var countryItems = countries
            .Select((country, index) => CountryFlagAssets.Create(
                country,
                index < countries.Count - 1))
            .ToArray();
        facts.Add(new DetailFactItem(
            "Страны",
            string.Join(", ", countryItems.Select(item => item.Name)),
            countryItems));
    }

    private static void AddAgeFact(List<DetailFactItem> facts, string? ageRating)
    {
        if (string.IsNullOrWhiteSpace(ageRating))
        {
            return;
        }

        var icon = AgeRatingAssets.Get(ageRating);
        facts.Add(icon is null
            ? new DetailFactItem("Возраст", ageRating)
            : new DetailFactItem("Возраст", ageRating, AgeIcon: icon));
    }

    /// <summary>
    /// Builds director cards, deferring avatar loads through the image cache.
    /// </summary>
    public static IReadOnlyList<PersonCardItem> BuildDirectors(
        CachedMediaMetadata metadata,
         Uri? referer,
        ImageCacheService images) =>
        metadata.Directors
            .Select(person => CreatePersonCard(person, referer, images))
            .ToArray();

    /// <summary>
    /// Builds cast cards (capped at 18 to match the original UI), deferring
    /// avatar loads through the image cache.
    /// </summary>
    public static IReadOnlyList<PersonCardItem> BuildCast(
        CachedMediaMetadata metadata,
         Uri? referer,
        ImageCacheService images) =>
        metadata.Cast
            .Take(18)
            .Select(person => CreatePersonCard(person, referer, images))
            .ToArray();

    /// <summary>
    /// Builds the external-rating cards (IMDb, Kinopoisk, etc.), formatting the
    /// numeric value and vote count the way the details UI expects.
    /// </summary>
    public static IReadOnlyList<ExternalRatingItem> BuildExternalRatings(CachedMediaMetadata metadata) =>
        metadata.ExternalRatings
            .Select(ratingItem =>
            {
                Uri.TryCreate(ratingItem.Url, UriKind.Absolute, out var ratingUrl);
                return new ExternalRatingItem(
                    ratingItem.Source,
                    ratingItem.Value?.ToString("0.0") ?? "—",
                    ratingItem.Votes is { } sourceVotes
                        ? $"{sourceVotes:N0} оценок"
                        : string.Empty,
                    ratingUrl);
            })
            .ToArray();

    /// <summary>
    /// Builds the episode schedule cards (capped at 16), normalising the
    /// per-episode label and missing dates.
    /// </summary>
    public static IReadOnlyList<ScheduleCardItem> BuildSchedule(CachedMediaMetadata metadata) =>
        metadata.Schedule
            .Take(16)
            .Select(item => new ScheduleCardItem(
                $"{item.Season} сезон · {item.Episode} серия",
                item.Title ?? item.OriginalTitle ?? "Без названия",
                item.ReleaseDate?.ToString("dd.MM.yyyy") ?? "Дата не указана",
                item.IsAvailable,
                item.IsWatched))
            .ToArray();

    /// <summary>
    /// Builds the "you might also like" recommendation cards (capped at 12),
    /// skipping entries whose URL cannot be parsed. <paramref name="createCard"/>
    /// is supplied by the view-model so each recommendation keeps its existing
    /// open behaviour (launching the title details flow).
    /// </summary>
    public static IReadOnlyList<MediaCardItem> BuildRecommendations(
        CachedMediaMetadata metadata,
        Func<string, Uri, Uri?, string, MediaCardItem> createCard)
    {
        var cards = new List<MediaCardItem>();
        foreach (var item in metadata.Recommendations.Take(12))
        {
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var recommendationUrl))
            {
                continue;
            }

            Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var recommendationImage);
            cards.Add(createCard(item.Title, recommendationUrl, recommendationImage, item.Category));
        }

        return cards;
    }

    /// <summary>
    /// Builds the grouped "collections / rankings / other parts" cards plus the
    /// layout hints (outer column count and per-group inner column count) the
    /// details UI uses to arrange them.
    /// </summary>
    public static (IReadOnlyList<DetailGroupCardItem> Groups, int ColumnCount) BuildDetailGroups(
        IReadOnlyList<string> collections,
        IReadOnlyList<string> rankings,
        IReadOnlyList<string> otherParts)
    {
        var groups = new[]
        {
            (Title: "Коллекции", Items: collections),
            (Title: "Место в подборках", Items: rankings),
            (Title: "Другие части", Items: otherParts)
        }.Where(group => group.Items.Count > 0).ToArray();

        var innerColumnCount = groups.Length switch
        {
            1 => 3,
            2 => 2,
            _ => 1
        };

        var cards = groups
            .Select(group => new DetailGroupCardItem(
                group.Title,
                group.Items,
                innerColumnCount))
            .ToArray();
        return (cards, Math.Max(groups.Length, 1));
    }

    private static PersonCardItem CreatePersonCard(
        CachedPerson person,
         Uri? referer,
        ImageCacheService images)
    {
        Uri.TryCreate(person.ImageUrl, UriKind.Absolute, out var imageUrl);
        return new PersonCardItem(
            person.Name,
            person.Job,
            images.Defer(imageUrl, referer, ImageDecodeSize.Avatar));
    }

    /// <summary>
    /// Formats a runtime the way the details UI presents it: hours+minutes for
    /// feature-length titles, minutes-only otherwise.
    /// </summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} ч {duration.Minutes} мин"
            : $"{duration.Minutes} мин";
}
