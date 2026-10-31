namespace RadioGolha;

/// <summary>Time helpers (port of fmt_time from radio_golha.py).</summary>
internal static class TimeText
{
    /// <summary>Convert milliseconds to mm:ss (or hh:mm:ss) text.</summary>
    public static string Format(long milliseconds)
    {
        var total = Math.Max(0L, milliseconds) / 1000L;
        if (total <= 0)
            return "00:00";

        var minutes = total / 60L;
        var seconds = total % 60L;
        var hours = minutes / 60L;
        minutes %= 60L;

        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }
}
