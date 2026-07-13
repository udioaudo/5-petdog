namespace DesktopSpike.Services;

public static class ClipboardSearchMatcher
{
    public static bool Matches(string kindLabel, string? summary, string? query)
    {
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return true;
        }

        return string.Equals(kindLabel, "文字", StringComparison.Ordinal)
            && (summary?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
