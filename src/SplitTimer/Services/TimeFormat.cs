namespace SplitTimer.Services;

public static class TimeFormat
{
    /// <summary>
    /// 统一读数格式：mm:ss.fff。毫秒向下截断，保证显示值不会大于真实成绩。
    /// </summary>
    public static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var totalMs = (long)elapsed.TotalMilliseconds;
        var minutes = totalMs / 60_000;
        var seconds = totalMs / 1000 % 60;
        var millis = totalMs % 1000;
        return $"{minutes:00}:{seconds:00}.{millis:000}";
    }

    public static string FormatOrPlaceholder(TimeSpan? elapsed, string placeholder = "--:--.---") =>
        elapsed.HasValue ? Format(elapsed.Value) : placeholder;
}
