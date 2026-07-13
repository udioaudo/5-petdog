namespace DesktopSpike.Models;

public sealed record AppSettings(
    string DisplayName,
    bool ClipboardPrivacyNoticeAccepted = false)
{
    public static AppSettings Default { get; } = new(Services.AppNameRules.DefaultDisplayName, false);
}
