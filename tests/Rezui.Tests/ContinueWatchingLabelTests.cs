using Rezui.ViewModels;
using Xunit;

namespace Rezui.Tests;

public sealed class ContinueWatchingLabelTests
{
    private static readonly DateOnly Today = new(2026, 8, 2);

    [Theory]
    [InlineData(2026, 8, 2, "Смотрели сегодня")]
    [InlineData(2026, 8, 1, "Смотрели вчера")]
    [InlineData(2026, 7, 31, "Смотрели позавчера")]
    [InlineData(2026, 7, 30, "Смотрели на этой неделе")]
    [InlineData(2026, 7, 15, "Смотрели 15.07.2026")]
    [InlineData(2025, 12, 31, "Смотрели в прошлом году")]
    [InlineData(2024, 1, 1, "Смотрели 01.01.2024")]
    public void FormatsCalendarPeriod(int year, int month, int day, string expected)
    {
        var actual = MainWindowViewModel.BuildLastViewedLabel(
            new DateOnly(year, month, day),
            null,
            Today);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FormatsCurrentMonthAfterCurrentWeek()
    {
        var actual = MainWindowViewModel.BuildLastViewedLabel(
            new DateOnly(2026, 8, 10),
            null,
            new DateOnly(2026, 8, 20));

        Assert.Equal("Смотрели в этом месяце", actual);
    }

    [Theory]
    [InlineData("сегодня", "Смотрели сегодня")]
    [InlineData("Вчера", "Смотрели вчера")]
    [InlineData("позавчера", "Смотрели позавчера")]
    [InlineData("31-07-2026", "Смотрели позавчера")]
    public void UnderstandsWebsiteDateLabels(string source, string expected)
    {
        var actual = MainWindowViewModel.BuildLastViewedLabel(null, source, Today);

        Assert.Equal(expected, actual);
    }
}
