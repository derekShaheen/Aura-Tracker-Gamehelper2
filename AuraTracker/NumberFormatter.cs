namespace AuraTracker;

internal static class NumberFormatter
{
    public static string Format(int value)
    {
        if (value >= 1_000_000_000)
        {
            return $"{value / 1_000_000_000f:0.##}B";
        }

        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000f:0.##}M";
        }

        if (value >= 1_000)
        {
            return $"{value / 1_000f:0.#}K";
        }

        return value.ToString();
    }

    public static string Format(long value)
    {
        if (value >= 1_000_000_000L)
        {
            return $"{value / 1_000_000_000f:0.##}B";
        }

        if (value >= 1_000_000L)
        {
            return $"{value / 1_000_000f:0.##}M";
        }

        if (value >= 1_000L)
        {
            return $"{value / 1_000f:0.#}K";
        }

        return value.ToString();
    }
}
