using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class DateTimeOffsetExtensionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(-300, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(42 * 60 + 30, "42 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(5 * 3600 + 59 * 60, "5 hours ago")]
    [InlineData(24 * 3600, "1 day ago")]
    [InlineData(25 * 3600, "1 day and 1 hour ago")]
    [InlineData(3 * 24 * 3600 + 4 * 3600 + 30 * 60, "3 days and 4 hours ago")]
    public void DescribesHowLongAgo(int secondsAgo, string expected)
    {
        Assert.Equal(expected, Now.AddSeconds(-secondsAgo).ToTimeAgo(Now));
    }
}
