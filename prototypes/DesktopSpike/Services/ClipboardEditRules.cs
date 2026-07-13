namespace DesktopSpike.Services;

public static class ClipboardEditRules
{
    public static bool IsValidText(string? text) => !string.IsNullOrWhiteSpace(text);
}
