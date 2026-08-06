using System.Collections.ObjectModel;
using HdRezka;
using Rezui.Models;
using Rezui.ViewModels;
using Serilog;

namespace Rezui.Services;

/// <summary>
/// Owns the comment loading pipeline for the title details page: paginated
/// fetching with stale-cache-first presentation, background refresh, and the
/// parent/child tree reconstruction. Extracted from MainWindowViewModel so the
/// 3.7k-line view-model no longer carries this ~250-line concern.
/// </summary>
/// <remarks>
/// The view-model still owns the <see cref="CommentNodeItem"/> collection and
/// the page counters (they bind directly to the UI), so the service reports
/// results back through <see cref="CommentBatch"/> and an apply callback rather
/// than mutating UI state itself. The current request version is passed in so
/// the service can bail out when the user has already navigated to another
/// title before a page arrives.
/// </remarks>
public sealed class CommentService
{
    private readonly RezkaClientService _rezka;
    private readonly ImageCacheService _images;
    private readonly ILogger _logger;
    private readonly Func<Media?> _currentMedia;
    private readonly Func<int> _currentRequestVersion;
    // Shared id -> node map across paginated loads so a reply arriving on a
    // later page can find the parent built on an earlier one.
    private Dictionary<long, CommentNodeItem> _nodeIndex = [];
    private Media? _loadingMedia;
    private bool _isLoading;

    public CommentService(
        RezkaClientService rezka,
        ImageCacheService images,
        Func<Media?> currentMedia,
        Func<int> currentRequestVersion,
        ILogger? logger = null)
    {
        _rezka = rezka;
        _images = images;
        _currentMedia = currentMedia;
        _currentRequestVersion = currentRequestVersion;
        _logger = logger ?? Log.ForContext<CommentService>();
    }

    /// <summary>True while a page is in flight; drives the UI spinner.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>
    /// Loads a single comment page. When <paramref name="replace"/> is set and
    /// a cached copy of page 1 exists, that stale page is presented immediately
    /// and a background refresh swaps in the fresh copy only if it changed.
    /// Returns the batch to apply, or <c>null</c> when the page was superseded
    /// (the user opened another title) or the request was cancelled.
    /// </summary>
    public async Task<CommentBatch?> LoadPageAsync(
        int page,
        bool replace,
        CancellationToken cancellationToken)
    {
        var media = _currentMedia();
        if (media is null)
        {
            return null;
        }

        if (_isLoading && ReferenceEquals(_loadingMedia, media))
        {
            return null;
        }

        CachedCommentPage? stale = null;
        if (replace && page == 1)
        {
            stale = await _rezka.GetCachedCommentsAsync(media, page, cancellationToken);
            if (!ReferenceEquals(_currentMedia(), media))
            {
                return null;
            }

            if (stale is not null)
            {
                var staleBatch = BuildBatch(stale, media, replace: true);
                _ = RefreshInBackgroundAsync(media, stale, cancellationToken);
                return staleBatch;
            }
        }

        return await FetchAsync(media, page, replace, cancellationToken);
    }

    /// <summary>
    /// Background refresh of page 1 once its cached copy is already on screen;
    /// returns a batch only when the fresh copy actually differs.
    /// </summary>
    private async Task<CommentBatch?> RefreshInBackgroundAsync(
        Media media,
        CachedCommentPage stale,
        CancellationToken cancellationToken)
    {
        _isLoading = true;
        _loadingMedia = media;
        try
        {
            var fresh = await Task.Run(
                () => _rezka.GetCommentsAsync(media, 1, cancellationToken),
                cancellationToken);
            if (!ReferenceEquals(_currentMedia(), media))
            {
                return null;
            }

            var unchanged = fresh.LastUpdateId == stale.LastUpdateId &&
                            fresh.Page == stale.Page &&
                            fresh.TotalPages == stale.TotalPages &&
                            fresh.Items.Count == stale.Items.Count;
            if (unchanged)
            {
                // Page counters moved even though the nodes did not.
                return new CommentBatch(
                    NewNodes: Array.Empty<CommentNodeItem>(),
                    Page: fresh.Page,
                    TotalPages: fresh.TotalPages,
                    ReplacedAll: false,
                    RefreshedNodes: false,
                    Superseded: false);
            }

            return BuildBatch(fresh, media, replace: true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Background comment refresh failed");
            return null;
        }
        finally
        {
            _isLoading = false;
            _loadingMedia = null;
        }
    }

    private async Task<CommentBatch?> FetchAsync(
        Media media,
        int page,
        bool replace,
        CancellationToken cancellationToken)
    {
        _isLoading = true;
        _loadingMedia = media;
        try
        {
            var result = await Task.Run(
                () => _rezka.GetCommentsAsync(media, page, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_currentMedia(), media))
            {
                return null;
            }

            return BuildBatch(result, media, replace);
        }
        finally
        {
            _isLoading = false;
            _loadingMedia = null;
        }
    }

    private CommentBatch BuildBatch(
        CachedCommentPage result,
        Media media,
        bool replace)
    {
        if (replace)
        {
            _nodeIndex = [];
        }

        var requestVersion = _currentRequestVersion();
        var newNodes = BuildCommentTree(
            result.Items,
            comment =>
            {
                var node = CreateNode(comment, media);
                return (comment.Id, comment.ParentId, node);
            },
            _nodeIndex);

        return new CommentBatch(
            NewNodes: newNodes,
            Page: result.Page,
            TotalPages: result.TotalPages,
            ReplacedAll: replace,
            RefreshedNodes: true,
            Superseded: requestVersion != _currentRequestVersion());
    }

    private CommentNodeItem CreateNode(CachedComment comment, Media media)
    {
        Uri.TryCreate(comment.AvatarUrl, UriKind.Absolute, out var avatarUrl);
        return new CommentNodeItem(
            comment.Id,
            comment.ParentId,
            comment.Depth,
            comment.Author,
            comment.DateLabel,
            comment.Text,
            comment.Likes,
            _images.Defer(avatarUrl, media.Url, ImageDecodeSize.Avatar));
    }

    /// <summary>
    /// Rebuilds a parent/child comment tree from the flat, in-nesting-order
    /// list returned by the website, linking replies through
    /// <see cref="CommentNodeItem.ParentId"/>. Existing nodes in
    /// <paramref name="existingIndex"/> are reused across paginated loads so a
    /// reply arriving on a later page attaches to the parent built earlier.
    /// Replies whose parent has not been seen yet are treated as roots rather
    /// than dropped, matching how the flat list degrades when pages are split.
    /// </summary>
    internal static IReadOnlyList<CommentNodeItem> BuildCommentTree<TComment>(
        IReadOnlyList<TComment> comments,
        Func<TComment, (long Id, long? ParentId, CommentNodeItem Node)> factory,
        Dictionary<long, CommentNodeItem> existingIndex)
    {
        var built = new List<(long Id, long? ParentId, CommentNodeItem Node)>(comments.Count);
        var batchNodes = new Dictionary<long, CommentNodeItem>();
        foreach (var comment in comments)
        {
            var entry = factory(comment);
            built.Add(entry);
            batchNodes[entry.Id] = entry.Node;
        }

        var roots = new List<CommentNodeItem>();
        foreach (var (id, parentId, node) in built)
        {
            if (parentId is { } parent &&
                batchNodes.TryGetValue(parent, out var batchParent))
            {
                batchParent.Children.Add(node);
            }
            else if (parentId is { } existingParent &&
                     existingIndex.TryGetValue(existingParent, out var knownParent))
            {
                knownParent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            existingIndex[id] = node;
        }

        return roots;
    }
}

/// <summary>
/// Result of loading one comment page: the roots to append (or the full
/// replacement set), the page counters, and whether the load was superseded by
/// a newer title before it completed.
/// </summary>
public sealed record CommentBatch(
    IReadOnlyList<CommentNodeItem> NewNodes,
    int Page,
    int TotalPages,
    bool ReplacedAll,
    bool RefreshedNodes,
    bool Superseded);
