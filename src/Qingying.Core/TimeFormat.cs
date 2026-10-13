namespace Qingying.Core;

public static class TimeFormat
{
    public static string Format(long milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var time = TimeSpan.FromMilliseconds(milliseconds);
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }

        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
