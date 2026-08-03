using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Rezui.Controls;
using Rezui.Models;
using Rezui.Views;
using Xunit;

namespace Rezui.Tests;

public sealed class ReactBitsMasonryPanelTests
{
    [AvaloniaFact]
    public void RealHomeTemplateKeepsItsMasonryHeightInsideItemsControl()
    {
        var templateOwner = new MainWindow();
        var template = Assert.IsAssignableFrom<IDataTemplate>(
            templateOwner.Resources["HomePosterCardTemplate"]);
        var cards = Enumerable.Range(0, 10)
            .Select(index => CreateHomeCard(index))
            .ToArray();
        var control = new ItemsControl
        {
            ItemsSource = cards,
            ItemsPanel = new FuncTemplate<Panel?>(() => CreatePanel()),
            ItemTemplate = template
        };
        var window = new Window { Width = 1240, Height = 1000, Content = control };

        window.Show();
        try
        {
            window.UpdateLayout();
            var panel = Assert.IsType<ReactBitsMasonryPanel>(control.ItemsPanelRoot);
            var containers = panel.Children.ToArray();

            Assert.Equal(cards.Length, containers.Length);
            Assert.Equal(330, cards[0].MasonryHeight);
            Assert.True(
                panel.DesiredSize.Height >= 650,
                $"panel desired={panel.DesiredSize.Height}, bounds={panel.Bounds.Height}");
            Assert.All(
                containers,
                container => Assert.True(
                    container.Bounds.Height >= 300,
                    $"bounds={container.Bounds.Height}, desired={container.DesiredSize.Height}, " +
                    $"height={container.Height}, min={container.MinHeight}, max={container.MaxHeight}, " +
                    $"data={container.DataContext?.GetType().FullName ?? "null"}, " +
                    $"content={(container as ContentPresenter)?.Content?.GetType().FullName ?? "null"}, " +
                    $"model={(container.DataContext as IMasonryItem)?.MasonryHeight}"));
            Assert.All(
                containers,
                container => Assert.IsAssignableFrom<IMasonryItem>(
                    container.DataContext ?? (container as ContentPresenter)?.Content));
        }
        finally
        {
            window.Close();
            templateOwner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ItemsControlContainersKeepTheExplicitMediaHeights()
    {
        var items = new[]
        {
            new TestMasonryItem(330),
            new TestMasonryItem(356),
            new TestMasonryItem(310),
            new TestMasonryItem(342),
            new TestMasonryItem(390),
            new TestMasonryItem(318)
        };
        var control = new ItemsControl
        {
            ItemsSource = items,
            ItemsPanel = new FuncTemplate<Panel?>(() => CreatePanel()),
            ItemTemplate = new FuncDataTemplate<TestMasonryItem>(
                (item, _) => new Border { Height = item.MasonryHeight })
        };
        var window = new Window { Width = 700, Height = 900, Content = control };

        window.Show();
        try
        {
            window.UpdateLayout();
            var panel = Assert.IsType<ReactBitsMasonryPanel>(control.ItemsPanelRoot);
            var containers = panel.Children.ToArray();

            Assert.Equal(items.Length, containers.Length);
            Assert.Equal(330, containers[0].Bounds.Height);
            Assert.Equal(356, containers[1].Bounds.Height);
            Assert.Equal(310, containers[2].Bounds.Height);
            Assert.All(containers, container => Assert.True(container.Bounds.Height >= 300));
            Assert.True(panel.DesiredSize.Height >= 700);

            await Task.Delay(620);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);
            Assert.All(containers, container => Assert.Equal(1, container.Opacity));
            Assert.All(
                containers,
                container => Assert.Equal(0, Assert.IsType<BlurEffect>(container.Effect).Radius));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AbsoluteTransformsPlaceEachCardIntoTheShortestColumn()
    {
        var panel = CreatePanel();
        var cards = new[]
        {
            CreateCard(100),
            CreateCard(150),
            CreateCard(80),
            CreateCard(120),
            CreateCard(90)
        };
        foreach (var card in cards)
        {
            panel.Children.Add(card);
        }

        var window = new Window { Width = 700, Height = 500, Content = panel };
        window.Show();
        try
        {
            window.UpdateLayout();
            var positions = cards
                .Select(card => Assert.IsType<TranslateTransform>(card.RenderTransform))
                .ToArray();

            Assert.Equal(0, positions[0].Y);
            Assert.Equal(0, positions[1].Y);
            Assert.Equal(0, positions[2].Y);
            Assert.Equal(90, positions[3].Y);
            Assert.Equal(110, positions[4].Y);
            Assert.True(positions[3].Y < cards[1].Bounds.Height);
            Assert.All(cards, card => Assert.True(card.Bounds.Width > 200));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ResponsiveWidthsUseFiveAndFourColumns()
    {
        var fiveColumnPanel = CreatePanel();
        var fiveColumnCards = Enumerable.Range(0, 6).Select(_ => CreateCard(100)).ToArray();
        foreach (var card in fiveColumnCards)
        {
            fiveColumnPanel.Children.Add(card);
        }
        var fiveColumnWindow = new Window
        {
            Width = 1200,
            Height = 500,
            Content = fiveColumnPanel
        };
        fiveColumnWindow.Show();
        try
        {
            fiveColumnWindow.UpdateLayout();
            var fiveColumnPosition = Assert.IsType<TranslateTransform>(
                fiveColumnCards[4].RenderTransform);
            Assert.True(fiveColumnPosition.X > 800);
        }
        finally
        {
            fiveColumnWindow.Close();
        }

        var fourColumnPanel = CreatePanel();
        var fourColumnCards = Enumerable.Range(0, 6).Select(_ => CreateCard(100)).ToArray();
        foreach (var card in fourColumnCards)
        {
            fourColumnPanel.Children.Add(card);
        }
        var fourColumnWindow = new Window
        {
            Width = 900,
            Height = 500,
            Content = fourColumnPanel
        };
        fourColumnWindow.Show();
        try
        {
            fourColumnWindow.UpdateLayout();
            var fourColumnPosition = Assert.IsType<TranslateTransform>(
                fourColumnCards[4].RenderTransform);
            Assert.Equal(0, fourColumnPosition.X);
            Assert.Equal(110, fourColumnPosition.Y);
        }
        finally
        {
            fourColumnWindow.Close();
        }
    }

    private static ReactBitsMasonryPanel CreatePanel() =>
        new()
        {
            MaxColumnCount = 5,
            ColumnSpacing = 10,
            RowSpacing = 10,
            EntranceDistance = 0,
            AnimationDuration = TimeSpan.Zero,
            Stagger = TimeSpan.Zero
        };

    private static Border CreateCard(double height) =>
        new() { DataContext = new TestMasonryItem(height) };

    private static HomeMediaCardItem CreateHomeCard(int position)
    {
        var media = new MediaCardItem(
            $"Тестовый тайтл {position}",
            new Uri($"https://example.com/media/{position}.html"),
            new DeferredImageSource(() => Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null)),
            "Фильм",
            () => Task.CompletedTask);
        return new HomeMediaCardItem(
            media,
            position,
            false,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);
    }

    private sealed record TestMasonryItem(double MasonryHeight) : IMasonryItem;
}
