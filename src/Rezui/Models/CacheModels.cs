namespace Rezui.Models;

public sealed record CachedNamedLink(string Name, string Url);

public sealed record CachedPerson(
    int Id,
    string Name,
    string Job,
    string Url,
    string? ImageUrl);

public sealed record CachedExternalRating(
    string Source,
    double? Value,
    int? Votes,
    string? Url);

public sealed record CachedScheduleEntry(
    long Id,
    int Season,
    int Episode,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    bool IsAvailable,
    bool IsWatched);

public sealed record CachedMediaPreview(
    string Title,
    string Url,
    string? ImageUrl,
    string Category,
    string? Details,
    string? Status);

public sealed record CachedTranslator(
    int Id,
    string Name,
    bool IsPremium,
    bool IsCamrip,
    bool HasAds,
    bool IsDirectorCut);

public sealed record CachedMediaMetadata(
    string Url,
    long Id,
    string Title,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> OriginalNames,
    string Description,
    string? ImageUrl,
    int? ReleaseYear,
    string Format,
    string Category,
    bool IsPlaybackAvailable,
    double? Rating,
    int? RatingVotes,
    string? Tagline,
    DateOnly? ReleaseDate,
    IReadOnlyList<CachedNamedLink> Countries,
    IReadOnlyList<CachedNamedLink> Genres,
    IReadOnlyList<CachedPerson> Directors,
    IReadOnlyList<CachedPerson> Cast,
    string? Quality,
    string? AgeRating,
    long? DurationSeconds,
    IReadOnlyList<CachedNamedLink> Collections,
    IReadOnlyList<CachedNamedLink> Rankings,
    IReadOnlyList<CachedExternalRating> ExternalRatings,
    IReadOnlyList<CachedMediaPreview> Recommendations,
    IReadOnlyList<CachedScheduleEntry> Schedule,
    IReadOnlyList<CachedTranslator> Translators,
    IReadOnlyList<CachedNamedLink> OtherParts);

public sealed record CachedComment(
    long Id,
    long? ParentId,
    int Depth,
    string Author,
    string? AvatarUrl,
    string DateLabel,
    string Text,
    int Likes,
    string Url);

public sealed record CachedCommentPage(
    IReadOnlyList<CachedComment> Items,
    int Page,
    int TotalPages,
    long? LastUpdateId);
