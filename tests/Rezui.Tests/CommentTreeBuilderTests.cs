using Avalonia.Media.Imaging;
using Rezui.Models;
using Rezui.ViewModels;
using Xunit;

namespace Rezui.Tests;

public sealed class CommentTreeBuilderTests
{
    private sealed record SourceComment(long Id, long? ParentId);

    private static DeferredImageSource DummyAvatar =>
        new(() => Task.FromResult<Bitmap?>(null));

    private static CommentNodeItem MakeNode(SourceComment source) =>
        new(
            source.Id,
            source.ParentId,
            depth: 0,
            author: $"author-{source.Id}",
            dateLabel: "сегодня",
            text: $"text-{source.Id}",
            likes: 0,
            avatarSource: DummyAvatar);

    private static (IReadOnlyList<CommentNodeItem> Roots, Dictionary<long, CommentNodeItem> Index)
        Build(IEnumerable<SourceComment> comments, Dictionary<long, CommentNodeItem>? index = null)
    {
        index ??= [];
        var roots = MainWindowViewModel.BuildCommentTree(
            comments.ToList(),
            source => (source.Id, source.ParentId, MakeNode(source)),
            index);
        return (roots, index);
    }

    [Fact]
    public void FlatTopLevelCommentsBecomeRoots()
    {
        var (roots, index) = Build(new[]
        {
            new SourceComment(1, null),
            new SourceComment(2, null),
        });

        Assert.Equal(new[] { 1L, 2L }, roots.Select(node => node.Id));
        Assert.True(roots.All(node => node.Children.Count == 0));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void RepliesAttachToParentByParentId()
    {
        var (roots, index) = Build(new[]
        {
            new SourceComment(1, null),
            new SourceComment(2, 1),
            new SourceComment(3, 1),
        });

        var root = Assert.Single(roots);
        Assert.Equal(1, root.Id);
        Assert.Equal(new[] { 2L, 3L }, root.Children.Select(child => child.Id));
        Assert.Equal(3, index.Count);
    }

    [Fact]
    public void DeeplyNestedRepliesPreserveHierarchy()
    {
        var (roots, index) = Build(new[]
        {
            new SourceComment(1, null),
            new SourceComment(2, 1),
            new SourceComment(3, 2),
            new SourceComment(4, 3),
        });

        var root = Assert.Single(roots);
        Assert.Equal(1, root.Id);
        var level1 = Assert.Single(root.Children);
        Assert.Equal(2, level1.Id);
        var level2 = Assert.Single(level1.Children);
        Assert.Equal(3, level2.Id);
        var level3 = Assert.Single(level2.Children);
        Assert.Equal(4, level3.Id);
        Assert.Empty(level3.Children);
    }

    [Fact]
    public void OrphanReplyWithoutKnownParentBecomesRoot()
    {
        // Reply arrives before (or without) its parent: it must not be dropped.
        var (roots, index) = Build(new[]
        {
            new SourceComment(10, 999),
            new SourceComment(11, null),
        });

        Assert.Equal(new[] { 10L, 11L }, roots.Select(node => node.Id));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void PaginationAppendsAndReattachesOrphans()
    {
        // Page 1 supplies the parent, page 2 supplies its reply. The shared
        // index lets the reply attach to the already-rendered parent without
        // the parent being returned as a new root again.
        var index = new Dictionary<long, CommentNodeItem>();
        var (firstRoots, _) = Build(
            new[] { new SourceComment(1, null) },
            index);
        var parent = Assert.Single(firstRoots);
        Assert.Empty(parent.Children);

        var (secondRoots, _) = Build(
            new[] { new SourceComment(2, 1) },
            index);

        // The reply is not a new root; it attached to the existing parent node.
        Assert.Empty(secondRoots);
        var reply = Assert.Single(parent.Children);
        Assert.Equal(2, reply.Id);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void EmptyBatchProducesNoRoots()
    {
        var (roots, _) = Build(Array.Empty<SourceComment>());

        Assert.Empty(roots);
    }

    [Fact]
    public void ReplyOrderWithinParentIsPreserved()
    {
        var (roots, _) = Build(new[]
        {
            new SourceComment(1, null),
            new SourceComment(2, 1),
            new SourceComment(3, 1),
            new SourceComment(4, 1),
        });

        var root = Assert.Single(roots);
        Assert.Equal(new[] { 2L, 3L, 4L }, root.Children.Select(child => child.Id));
    }

    [Fact]
    public void NodeCarriesSourceIdentityFields()
    {
        var node = new CommentNodeItem(
            id: 42,
            parentId: 7,
            depth: 2,
            author: "Freak",
            dateLabel: "вчера",
            text: "Крутой сериал",
            likes: 13,
            avatarSource: DummyAvatar);

        Assert.Equal(42, node.Id);
        Assert.Equal(7, node.ParentId);
        Assert.Equal(2, node.Depth);
        Assert.Equal("Freak", node.Author);
        Assert.Equal(13, node.Likes);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    [InlineData(3, true, true)]
    [InlineData(4, true, false)]
    public void ToggleButtonVisibilityMatchesReplyCount(
        int replyCount,
        bool hasReplies,
        bool canToggleBranch)
    {
        var node = new CommentNodeItem(1, null, 0, "a", "d", "t", 0, DummyAvatar);
        for (var i = 2; i < 2 + replyCount; i++)
        {
            node.Children.Add(new CommentNodeItem(i, 1, 1, "b", "d", "t", 0, DummyAvatar));
        }

        node.NotifyChildrenChanged();

        Assert.Equal(hasReplies, node.HasReplies);
        Assert.Equal(canToggleBranch, node.CanToggleBranch);
    }

    [Fact]
    public void ShortThreadTogglesWholeBranch()
    {
        var node = new CommentNodeItem(1, null, 0, "a", "d", "t", 0, DummyAvatar);
        node.Children.Add(new CommentNodeItem(2, 1, 1, "b", "d", "t", 0, DummyAvatar));
        node.Children.Add(new CommentNodeItem(3, 1, 1, "c", "d", "t", 0, DummyAvatar));

        Assert.False(node.HasOverflow);
        Assert.Equal(2, node.RenderedReplies.Count);
        Assert.Empty(node.OverflowReplies);

        node.IsExpanded = false;
        Assert.Empty(node.RenderedReplies);
        Assert.Contains("Показать ответы (2)", node.CollapseLabel);

        node.IsExpanded = true;
        Assert.Equal(2, node.RenderedReplies.Count);
        Assert.Equal("Свернуть ответы", node.CollapseLabel);
    }

    [Fact]
    public void LongThreadShowsThreeAlwaysAndOverflowBehindButton()
    {
        var node = new CommentNodeItem(1, null, 0, "a", "d", "t", 0, DummyAvatar);
        for (var i = 2; i <= 6; i++)
        {
            node.Children.Add(new CommentNodeItem(i, 1, 1, $"a{i}", "d", "t", 0, DummyAvatar));
        }

        node.NotifyChildrenChanged();
        Assert.True(node.HasOverflow);

        // First three always visible, collapse state does not affect them.
        Assert.Equal(3, node.RenderedReplies.Count);
        node.IsExpanded = false;
        Assert.Equal(3, node.RenderedReplies.Count);
        Assert.Contains("Другие ответы (2)", node.OverflowLabel);

        // Overflow visible only when expanded.
        node.IsExpanded = true;
        Assert.Equal(2, node.OverflowReplies.Count);
        Assert.Equal("Свернуть", node.OverflowLabel);

        node.IsExpanded = false;
        Assert.Empty(node.OverflowReplies);
    }
}
