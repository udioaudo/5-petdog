namespace DesktopSpike.Services;

public static class AppNameRules
{
    public const string DefaultDisplayName = "小狗效率屋";
    public const int MaximumLength = 24;

    public static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaximumLength
            ? null
            : normalized;
    }
}
