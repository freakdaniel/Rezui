using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Rezui.Models;
using Rezui.Views;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Rezui.Tests;

public sealed class CommentLayoutTests
{
    private static DeferredImageSource DummyAvatar =>
        new(() => Task.FromResult<Bitmap?>(null));

    [AvaloniaFact]
    public void NestedReplyUsesCompactIndentAndUniformThreadStroke()
    {
        var parent = new CommentNodeItem(1, null, 0, "parent", "", "text", 0, DummyAvatar);
        parent.Children.Add(new CommentNodeItem(2, 1, 1, "reply", "", "text", 0, DummyAvatar));
        parent.NotifyChildrenChanged();

        var mainWindow = new MainWindow();
        var template = Assert.IsAssignableFrom<IDataTemplate>(
            mainWindow.Resources["CommentNodeTemplate"]);
        var comment = Assert.IsType<Grid>(template.Build(parent));
        try
        {
            var replies = comment.Children
                .OfType<StackPanel>()
                .Single(panel => Grid.GetRow(panel) == 1 && Grid.GetColumn(panel) == 1);
            Assert.Equal(38, comment.ColumnDefinitions[0].Width.Value);
            Assert.Equal(0, replies.Margin.Left);
            Assert.Equal(16, replies.Margin.Top);

            var toggleRow = comment.Children
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("comment-toggle-row"));
            var elbow = toggleRow.Children
                .OfType<ShapePath>()
                .Single(path => path.Classes.Contains("comment-thread-elbow"));
            Assert.Equal(2, elbow.StrokeThickness);
            Assert.Equal(16, elbow.Margin.Top);

            var toggles = toggleRow.Children
                .OfType<Button>()
                .Where(button => button.Classes.Contains("comment-toggle"));
            Assert.All(toggles, button => Assert.Equal(16, button.Margin.Top));

        }
        finally
        {
            mainWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task NestedReplyHasBalancedVerticalSpacing()
    {
        var parent = new CommentNodeItem(1, null, 0, "parent", "", "text", 0, DummyAvatar);
        var reply = new CommentNodeItem(2, 1, 1, "reply", "", "text", 0, DummyAvatar);
        parent.Children.Add(reply);
        parent.NotifyChildrenChanged();

        var resourcesWindow = new MainWindow();
        var template = Assert.IsAssignableFrom<IDataTemplate>(
            resourcesWindow.Resources["CommentNodeTemplate"]);
        var presenter = new ContentControl
        {
            Content = parent,
            ContentTemplate = template
        };
        var host = new Window
        {
            Width = 700,
            Height = 500,
            Content = presenter
        };
        host.Resources["CommentNodeTemplate"] = template;

        host.Show();
        try
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            host.UpdateLayout();

            var nodes = presenter.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Classes.Contains("comment-node"))
                .ToArray();
            Assert.Equal(2, nodes.Length);

            var parentNode = nodes.Single(node => ReferenceEquals(node.DataContext, parent));
            var replyNode = nodes.Single(node => ReferenceEquals(node.DataContext, reply));
            var parentContent = parentNode.Children
                .OfType<StackPanel>()
                .Single(panel => panel.Classes.Contains("comment-content"));
            var replyContent = replyNode.Children
                .OfType<StackPanel>()
                .Single(panel => panel.Classes.Contains("comment-content"));
            var toggleRow = parentNode.Children
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("comment-toggle-row"));
            var replyToggleRow = replyNode.Children
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("comment-toggle-row"));
            var replyReplies = replyNode.Children
                .OfType<StackPanel>()
                .Single(panel => Grid.GetRow(panel) == 1);
            var toggle = toggleRow.Children
                .OfType<Button>()
                .Single(button => button.IsVisible);

            var replyTop = replyNode.TranslatePoint(default, parentNode)!.Value.Y;
            var parentContentTop = parentContent.TranslatePoint(default, parentNode)!.Value.Y;
            var replyContentTop = replyContent.TranslatePoint(default, parentNode)!.Value.Y;
            var toggleTop = toggle.TranslatePoint(default, parentNode)!.Value.Y;
            var topGap = replyTop - (parentContentTop + parentContent.Bounds.Height);
            var bottomGap = toggleTop - (replyContentTop + replyContent.Bounds.Height);

            Assert.True(
                Math.Abs(topGap - bottomGap) <= 0.1,
                $"top={topGap}, bottom={bottomGap}, replyTop={replyTop}, " +
                $"replyNodeHeight={replyNode.Bounds.Height}, replyContentTop={replyContentTop}, " +
                $"replyContentHeight={replyContent.Bounds.Height}, toggleTop={toggleTop}, " +
                $"toggleRowTop={toggleRow.TranslatePoint(default, parentNode)!.Value.Y}, " +
                $"replyDesired={replyNode.DesiredSize}, replyRepliesVisible={replyReplies.IsVisible}, " +
                $"replyToggleVisible={replyToggleRow.IsVisible}, " +
                $"replyRows={string.Join(',', replyNode.RowDefinitions.Select(row => $"{row.Height}:{row.ActualHeight}"))}, " +
                $"children={string.Join(';', replyNode.Children.Select(child => $"{child.GetType().Name}[r{Grid.GetRow(child)},s{Grid.GetRowSpan(child)}]:v{child.IsVisible}:d{child.DesiredSize}:b{child.Bounds}"))}");
            Assert.Equal(16, topGap, precision: 1);
        }
        finally
        {
            host.Close();
            resourcesWindow.Close();
        }
    }

    [AvaloniaFact]
    public void DetailsPageDoesNotScrollFocusedControlsIntoView()
    {
        var window = new MainWindow();
        try
        {
            var details = Assert.IsType<ScrollViewer>(
                window.FindControl<ScrollViewer>("DetailsScrollViewer"));
            Assert.False(details.BringIntoViewOnFocusChange);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DetailsGridsDoNotRegisterVirtualizedScrollAnchors()
    {
        var window = new MainWindow();
        try
        {
            var listNames = new[]
            {
                "DetailsDirectorsList",
                "DetailsCastList",
                "DetailsScheduleList",
                "DetailsRecommendationsList"
            };

            foreach (var name in listNames)
            {
                var list = Assert.IsAssignableFrom<Control>(
                    window.FindControl<Control>(name));
                Assert.IsType<ItemsControl>(list);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
