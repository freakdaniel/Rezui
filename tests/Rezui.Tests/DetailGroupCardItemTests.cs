using Rezui.Models;
using Xunit;

namespace Rezui.Tests;

public sealed class DetailGroupCardItemTests
{
    [Fact]
    public void ConstructorDistributesItemsAcrossBalancedInvisibleColumns()
    {
        var card = new DetailGroupCardItem(
            "Другие части",
            Enumerable.Range(1, 10).Select(value => value.ToString()).ToArray(),
            3);

        Assert.Equal(3, card.ColumnCount);
        Assert.Equal([4, 3, 3], card.Columns.Select(column => column.Items.Count));
        Assert.Equal(
            Enumerable.Range(1, 10).Select(value => value.ToString()),
            card.Columns.SelectMany(column => column.Items));
    }

    [Fact]
    public void ConstructorDoesNotCreateMoreColumnsThanItems()
    {
        var card = new DetailGroupCardItem("Коллекции", ["Одна"], 3);

        Assert.Single(card.Columns);
        Assert.Equal("Одна", Assert.Single(card.Columns[0].Items));
    }
}
