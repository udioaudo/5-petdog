using DesktopSpike.Models;

namespace DesktopSpike.Services;

public sealed class ClipboardEventDeduplicator
{
    private string? _lastFingerprint;
    private string? _pendingSelfFingerprint;
    private DateTimeOffset _pendingSelfExpiresAt;

    public void MarkSelfWrite(string fingerprint, DateTimeOffset now)
    {
        _pendingSelfFingerprint = fingerprint;
        _pendingSelfExpiresAt = now.AddSeconds(2);
    }

    public ClipboardObservation Observe(string fingerprint, DateTimeOffset now)
    {
        if (_pendingSelfFingerprint is not null && now <= _pendingSelfExpiresAt &&
            string.Equals(_pendingSelfFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _lastFingerprint = fingerprint;
            return ClipboardObservation.SelfWrite;
        }

        if (now > _pendingSelfExpiresAt)
        {
            _pendingSelfFingerprint = null;
        }

        if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return ClipboardObservation.Duplicate;
        }

        _lastFingerprint = fingerprint;
        return ClipboardObservation.New;
    }
}
