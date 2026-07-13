using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopSpike.Models;

public sealed class ClipboardHistoryItemRow : INotifyPropertyChanged
{
    private ClipboardSnapshot _snapshot;
    private string _summary;
    private string _editText;
    private bool _isEditing;
    private DateTimeOffset _lastCopiedAt;
    private bool _isPinned;
    private bool _imageLoadFailed;

    public ClipboardHistoryItemRow(ClipboardSnapshot snapshot)
        : this(Guid.NewGuid(), snapshot, snapshot.CapturedAt, false, false, null, false)
    {
    }

    public ClipboardHistoryItemRow(ClipboardTextRecord record)
        : this(
            record.Id,
            new ClipboardSnapshot(
                ClipboardContentKind.Text,
                record.LastCopiedAt,
                Services.ClipboardPreviewFormatter.ForText(record.Text),
                record.Fingerprint,
                record.Text),
            record.LastCopiedAt,
            record.IsPinned,
            true,
            null,
            false)
    {
    }

    public ClipboardHistoryItemRow(ClipboardImageRecord record)
        : this(
            record.Id,
            new ClipboardSnapshot(
                ClipboardContentKind.Image,
                record.LastCopiedAt,
                $"图片 {record.PixelWidth} × {record.PixelHeight}",
                record.Fingerprint,
                ImagePayload: record.Thumbnail),
            record.LastCopiedAt,
            record.IsPinned,
            true,
            record.OriginalFileName,
            record.PreviewLoadFailed)
    {
    }

    private ClipboardHistoryItemRow(
        Guid id,
        ClipboardSnapshot snapshot,
        DateTimeOffset lastCopiedAt,
        bool isPinned,
        bool isPersisted,
        string? originalImageFileName,
        bool imageLoadFailed)
    {
        Id = id;
        _snapshot = snapshot;
        _summary = snapshot.Summary;
        _editText = snapshot.TextPayload ?? string.Empty;
        _lastCopiedAt = lastCopiedAt;
        _isPinned = isPinned;
        _imageLoadFailed = imageLoadFailed;
        IsPersisted = isPersisted;
        OriginalImageFileName = originalImageFileName;
        KindLabel = snapshot.Kind == ClipboardContentKind.Text ? "文字" : "图片";
    }

    public Guid Id { get; }
    public string KindLabel { get; }
    public bool IsText => _snapshot.Kind == ClipboardContentKind.Text;
    public bool IsPersisted { get; }
    public string? OriginalImageFileName { get; private set; }
    public ClipboardSnapshot Snapshot => _snapshot;
    public string SearchText => _snapshot.TextPayload ?? _summary;
    public string TimeLabel => LastCopiedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public bool HasImagePreview => !IsText && _snapshot.ImagePayload is not null;
    public string ImageStatusText => ImageLoadFailed ? "图片文件暂时无法加载" : Summary;

    public DateTimeOffset LastCopiedAt
    {
        get => _lastCopiedAt;
        private set
        {
            if (SetField(ref _lastCopiedAt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeLabel)));
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        private set => SetField(ref _isPinned, value);
    }

    public bool ImageLoadFailed
    {
        get => _imageLoadFailed;
        private set
        {
            if (SetField(ref _imageLoadFailed, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageStatusText)));
            }
        }
    }

    public string Summary
    {
        get => _summary;
        private set
        {
            if (SetField(ref _summary, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageStatusText)));
            }
        }
    }

    public string EditText
    {
        get => _editText;
        set => SetField(ref _editText, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    public void ResetEditText() => EditText = _snapshot.TextPayload ?? string.Empty;

    public void UpdateLastCopiedAt(DateTimeOffset value)
    {
        LastCopiedAt = value;
        _snapshot = _snapshot with { CapturedAt = value };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
    }

    public void SetPinned(bool value) => IsPinned = value;

    public void ApplyPersistedRecord(ClipboardTextRecord record)
    {
        if (!IsText || record.Id != Id)
        {
            throw new InvalidOperationException("无法将记录应用到当前剪贴板卡片。");
        }
        _snapshot = _snapshot with
        {
            CapturedAt = record.LastCopiedAt,
            TextPayload = record.Text,
            Summary = Services.ClipboardPreviewFormatter.ForText(record.Text),
            Fingerprint = record.Fingerprint
        };
        Summary = _snapshot.Summary;
        EditText = record.Text;
        LastCopiedAt = record.LastCopiedAt;
        IsPinned = record.IsPinned;
        NotifySnapshotChanged();
    }

    public void ApplyPersistedImageRecord(ClipboardImageRecord record)
    {
        if (IsText || record.Id != Id)
        {
            throw new InvalidOperationException("无法将图片记录应用到当前剪贴板卡片。");
        }
        _snapshot = new ClipboardSnapshot(
            ClipboardContentKind.Image,
            record.LastCopiedAt,
            $"图片 {record.PixelWidth} × {record.PixelHeight}",
            record.Fingerprint,
            ImagePayload: record.Thumbnail);
        OriginalImageFileName = record.OriginalFileName;
        Summary = _snapshot.Summary;
        LastCopiedAt = record.LastCopiedAt;
        IsPinned = record.IsPinned;
        ImageLoadFailed = record.PreviewLoadFailed;
        NotifySnapshotChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImagePreview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalImageFileName)));
    }

    public void ApplyTextEdit(string text)
    {
        if (!IsText)
        {
            throw new InvalidOperationException("图片记录不支持文字编辑。");
        }

        _snapshot = _snapshot with
        {
            TextPayload = text,
            Summary = Services.ClipboardPreviewFormatter.ForText(text),
            Fingerprint = Services.ClipboardFingerprint.ForText(text)
        };
        Summary = _snapshot.Summary;
        EditText = text;
        NotifySnapshotChanged();
    }

    private void NotifySnapshotChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
