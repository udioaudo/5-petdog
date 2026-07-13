namespace DesktopSpike.Services;

public static class TodoRules
{
    public static string? Normalize(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
