using System.Globalization;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

public static class DateTimeOffsetExtensions
{
    /// <summary>
    /// Describes how long ago <paramref name="at"/> was, relative to <paramref name="now"/>:
    /// "just now", "5 minutes ago", "3 hours ago" or "2 days and 4 hours ago".
    /// Moments in the future (clock skew) read as "just now".
    /// </summary>
    public static string ToTimeAgo(this DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Count((int)elapsed.TotalMinutes, "minute")} ago";
        }
        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Count((int)elapsed.TotalHours, "hour")} ago";
        }
        var days = Count((int)elapsed.TotalDays, "day");
        return elapsed.Hours == 0 ? $"{days} ago" : $"{days} and {Count(elapsed.Hours, "hour")} ago";
    }

    /// <inheritdoc cref="ToTimeAgo(DateTimeOffset, DateTimeOffset)"/>
    public static string ToTimeAgo(this DateTimeOffset at, TimeProvider time)
    {
        return at.ToTimeAgo(time.GetUtcNow());
    }

    /// <summary>The full local date and time, for showing next to (or behind) a relative time.</summary>
    public static string ToLocalDateTimeText(this DateTimeOffset at)
    {
        return at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Count(int value, string unit)
    {
        return value == 1 ? $"1 {unit}" : $"{value} {unit}s";
    }
}
