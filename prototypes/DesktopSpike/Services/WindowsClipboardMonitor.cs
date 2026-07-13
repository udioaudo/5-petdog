using DesktopSpike.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DesktopSpike.Services;

public sealed class WindowsClipboardMonitor : IClipboardMonitor
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly ClipboardEventDeduplicator _deduplicator = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _disposed;

    public event EventHandler<ClipboardSnapshot>? SnapshotCaptured;
    public event EventHandler<string>? StatusChanged;

    public ClipboardSnapshot? LastSnapshot { get; private set; }

    public void Start(Window owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not null)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(owner).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("窗口句柄尚未创建。");
        }

        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("无法连接窗口消息源。");
        _source.AddHook(WindowMessageHook);

        if (!AddClipboardFormatListener(_windowHandle))
        {
            _source.RemoveHook(WindowMessageHook);
            _source = null;
            throw new InvalidOperationException("Windows拒绝注册剪贴板监听器。");
        }

        StatusChanged?.Invoke(this, "剪贴板监听已启动。");
    }

    public void Stop()
    {
        if (_source is null)
        {
            return;
        }

        RemoveClipboardFormatListener(_windowHandle);
        _source.RemoveHook(WindowMessageHook);
        _source = null;
        _windowHandle = IntPtr.Zero;
        StatusChanged?.Invoke(this, "剪贴板监听已停止。");
    }

    public async Task<bool> CopyBackAsync(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _deduplicator.MarkSelfWrite(snapshot.Fingerprint, DateTimeOffset.Now);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (snapshot.Kind == ClipboardContentKind.Text && snapshot.TextPayload is not null)
                {
                    Clipboard.SetText(snapshot.TextPayload);
                }
                else if (snapshot.Kind == ClipboardContentKind.Image && snapshot.ImagePayload is not null)
                {
                    Clipboard.SetImage(snapshot.ImagePayload);
                }
                else
                {
                    return false;
                }

                StatusChanged?.Invoke(this, "已重新写入系统剪贴板，等待验证回流事件。");
                return true;
            }
            catch (ExternalException) when (attempt < 3)
            {
                await Task.Delay(60 * attempt);
            }
        }

        StatusChanged?.Invoke(this, "系统剪贴板正被其他程序占用，请稍后重试。");
        return false;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            _ = CaptureWithRetryAsync();
        }

        return IntPtr.Zero;
    }

    private async Task CaptureWithRetryAsync()
    {
        await _captureGate.WaitAsync();
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await CaptureCurrentAsync();
                    return;
                }
                catch (ExternalException) when (attempt < 3)
                {
                    await Task.Delay(50 * attempt);
                }
                catch (Exception exception)
                {
                    StatusChanged?.Invoke(this, $"本次剪贴板事件未读取：{exception.GetType().Name}");
                    return;
                }
            }

            StatusChanged?.Invoke(this, "剪贴板暂时被占用，本次事件已安全跳过。");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task CaptureCurrentAsync()
    {
        ClipboardSnapshot? snapshot;

        if (Clipboard.ContainsFileDropList())
        {
            StatusChanged?.Invoke(this, "检测到文件或文件夹：第一版不记录此类型。");
            return;
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            var preview = ClipboardPreviewFormatter.ForText(text);
            if (preview.Length == 0)
            {
                StatusChanged?.Invoke(this, "空文字已忽略。");
                return;
            }

            snapshot = new ClipboardSnapshot(
                ClipboardContentKind.Text,
                DateTimeOffset.Now,
                preview,
                ClipboardFingerprint.ForText(text),
                TextPayload: text);
        }
        else if (Clipboard.ContainsImage())
        {
            var source = Clipboard.GetImage();
            if (source is null)
            {
                StatusChanged?.Invoke(this, "检测到图片格式，但无法读取图片。");
                return;
            }

            var image = new WriteableBitmap(source);
            image.Freeze();
            var fingerprint = await Task.Run(() => ClipboardFingerprint.ForImage(image));
            snapshot = new ClipboardSnapshot(
                ClipboardContentKind.Image,
                DateTimeOffset.Now,
                $"图片 {image.PixelWidth} × {image.PixelHeight}",
                fingerprint,
                ImagePayload: image);
        }
        else
        {
            StatusChanged?.Invoke(this, "检测到暂不支持的剪贴板格式，已安全忽略。");
            return;
        }

        var observation = _deduplicator.Observe(snapshot.Fingerprint, snapshot.CapturedAt);
        switch (observation)
        {
            case ClipboardObservation.SelfWrite:
                StatusChanged?.Invoke(this, "自身重新复制产生的回流事件已成功抑制。");
                return;
            case ClipboardObservation.Duplicate:
                SnapshotCaptured?.Invoke(this, snapshot);
                StatusChanged?.Invoke(this, "检测到重复内容，已更新最近复制时间。");
                return;
            default:
                LastSnapshot = snapshot;
                SnapshotCaptured?.Invoke(this, snapshot);
                StatusChanged?.Invoke(this, snapshot.Kind == ClipboardContentKind.Text
                    ? "已捕获文字剪贴板事件。"
                    : "已捕获图片剪贴板事件。");
                return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _captureGate.Dispose();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}

