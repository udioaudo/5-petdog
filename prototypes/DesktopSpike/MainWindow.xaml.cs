using DesktopSpike.Models;
using DesktopSpike.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace DesktopSpike;

public partial class MainWindow : Window
{
    private readonly WindowPlacementService _placementService = new();
    private readonly DisplayWorkAreaService _displayService = new();
    private readonly WindowSettingsService _settingsService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly IClipboardMonitor _clipboardMonitor = new WindowsClipboardMonitor();
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _imagePersistenceGate = new(1, 1);
    private TodoRepository? _todoRepository;
    private ClipboardTextRepository? _clipboardTextRepository;
    private ClipboardImageRepository? _clipboardImageRepository;
    private AppSettings _appSettings = AppSettings.Default;
    private string _displayName = AppNameRules.DefaultDisplayName;
    private bool _clipboardMonitorStarted;
    private bool _suppressTodoCompletionChange;
    private Rect _expandedBounds;
    private bool _expanded = true;
    private bool _initialized;
    private bool _applyingGeometry;

    public ObservableCollection<ClipboardHistoryItemRow> ClipboardEvents { get; } = [];
    public ObservableCollection<TodoItemRow> TodoItems { get; } = [];
    public ICollectionView ClipboardEventsView { get; }
    public ListCollectionView CurrentTodoItemsView { get; }
    public ListCollectionView CompletedTodoItemsView { get; }

    public MainWindow()
    {
        InitializeComponent();
        _appSettings = _appSettingsService.Load();
        ApplyDisplayName(_appSettings.DisplayName);

        ClipboardEventsView = CollectionViewSource.GetDefaultView(ClipboardEvents);
        ClipboardEventsView.Filter = FilterClipboardEvent;
        ClipboardEventsView.SortDescriptions.Add(new SortDescription(nameof(ClipboardHistoryItemRow.IsPinned), ListSortDirection.Descending));
        ClipboardEventsView.SortDescriptions.Add(new SortDescription(nameof(ClipboardHistoryItemRow.LastCopiedAt), ListSortDirection.Descending));

        CurrentTodoItemsView = new ListCollectionView(TodoItems)
        {
            Filter = item => item is TodoItemRow row && !row.IsCompleted
        };
        CurrentTodoItemsView.SortDescriptions.Add(new SortDescription(nameof(TodoItemRow.CreatedAt), ListSortDirection.Descending));

        CompletedTodoItemsView = new ListCollectionView(TodoItems)
        {
            Filter = item => item is TodoItemRow row && row.IsCompleted
        };
        CompletedTodoItemsView.SortDescriptions.Add(new SortDescription(nameof(TodoItemRow.CompletedAt), ListSortDirection.Descending));
        CompletedTodoItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TodoItemRow.CompletedDateLabel)));

        DataContext = this;
        InitializeTodoStorage();
        InitializeClipboardStorage();
        UpdateTodoState();
        UpdateClipboardState();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += SaveTimer_Tick;

        SourceInitialized += OnSourceInitialized;
        LocationChanged += OnGeometryChanged;
        SizeChanged += OnGeometryChanged;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _clipboardMonitor.SnapshotCaptured += OnSnapshotCaptured;
        _clipboardMonitor.StatusChanged += OnStatusChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var displays = _displayService.GetDisplays();
        _expandedBounds = _placementService.Restore(_settingsService.Load(), displays);
        ApplyExpandedBounds(_expandedBounds, displays);
        _initialized = true;

        if (_appSettings.ClipboardPrivacyNoticeAccepted)
        {
            StartClipboardMonitor();
        }
        else
        {
            PrivacyOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "请先阅读剪贴板隐私说明；确认前不会记录新的复制内容。";
        }
    }

    private void StartClipboardMonitor()
    {
        if (_clipboardMonitorStarted)
        {
            return;
        }
        try
        {
            _clipboardMonitor.Start(this);
            _clipboardMonitorStarted = true;
            StatusText.Text = "剪贴板监听已启动；文字和图片历史会保存在本机。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"剪贴板监听启动失败：{exception.GetType().Name}";
        }
    }

    private void AcceptPrivacy_Click(object sender, RoutedEventArgs e)
    {
        var updated = _appSettings with { ClipboardPrivacyNoticeAccepted = true };
        if (!_appSettingsService.TrySave(updated))
        {
            PrivacyValidationText.Text = "隐私确认暂时无法保存，请检查本机文件权限后重试。";
            return;
        }
        _appSettings = updated;
        PrivacyValidationText.Text = string.Empty;
        PrivacyOverlay.Visibility = Visibility.Collapsed;
        StartClipboardMonitor();
    }

    private void DeclinePrivacy_Click(object sender, RoutedEventArgs e)
    {
        PrivacyValidationText.Text = string.Empty;
        PrivacyOverlay.Visibility = Visibility.Collapsed;
        StatusText.Text = "本次运行未启用剪贴板记录；重新启动后可再次选择。";
    }

    private void ApplyDisplayName(string name)
    {
        _displayName = AppNameRules.Normalize(name) ?? AppNameRules.DefaultDisplayName;
        DisplayNameText.Text = _displayName;
        DisplayNameText.ToolTip = _displayName;
        Title = _displayName;
        CollapsedPanel.ToolTip = $"展开{_displayName}";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsNameTextBox.Text = _displayName;
        SettingsValidationText.Text = string.Empty;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsNameTextBox.Focus();
        SettingsNameTextBox.SelectAll();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        SettingsValidationText.Text = string.Empty;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveDisplayName();

    private void SettingsName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveDisplayName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void RestoreDefaultName_Click(object sender, RoutedEventArgs e)
    {
        SettingsNameTextBox.Text = AppNameRules.DefaultDisplayName;
        SettingsValidationText.Text = string.Empty;
        SettingsNameTextBox.Focus();
        SettingsNameTextBox.SelectAll();
    }

    private void SaveDisplayName()
    {
        var normalized = AppNameRules.Normalize(SettingsNameTextBox.Text);
        if (normalized is null)
        {
            SettingsValidationText.Text = $"请输入1至{AppNameRules.MaximumLength}个字符的名称。";
            SettingsNameTextBox.Focus();
            return;
        }

        var updatedSettings = _appSettings with { DisplayName = normalized };
        if (!_appSettingsService.TrySave(updatedSettings))
        {
            SettingsValidationText.Text = "名称暂时无法保存，请检查本机文件权限后重试。";
            return;
        }

        _appSettings = updatedSettings;
        ApplyDisplayName(normalized);
        SettingsOverlay.Visibility = Visibility.Collapsed;
        StatusText.Text = $"名称已更新为“{normalized}”，并保存在本机。";
    }

    private void OnSnapshotCaptured(object? sender, ClipboardSnapshot snapshot)
    {
        if (snapshot.Kind == ClipboardContentKind.Text && snapshot.TextPayload is not null)
        {
            Dispatcher.Invoke(() =>
            {
                CaptureTextSnapshot(snapshot);
                ClipboardEventsView.Refresh();
                UpdateClipboardState();
            });
            return;
        }

        _ = CaptureImageSnapshotAsync(snapshot);
    }

    private void CaptureTextSnapshot(ClipboardSnapshot snapshot)
    {
        ClipboardTextRecord? persisted = null;
        if (_clipboardTextRepository is not null)
        {
            try
            {
                persisted = _clipboardTextRepository.Capture(
                    snapshot.TextPayload!, snapshot.Fingerprint, snapshot.CapturedAt);
            }
            catch
            {
                _clipboardTextRepository = null;
                StatusText.Text = "文字历史暂时无法写入本机，已切换为本次运行有效模式。";
            }
        }

        var existing = ClipboardEvents.FirstOrDefault(row =>
            row.IsText && string.Equals(row.Snapshot.Fingerprint, snapshot.Fingerprint, StringComparison.Ordinal));
        if (persisted is not null)
        {
            existing = ClipboardEvents.FirstOrDefault(row => row.Id == persisted.Id) ?? existing;
            if (existing is null)
            {
                ClipboardEvents.Add(new ClipboardHistoryItemRow(persisted));
            }
            else
            {
                existing.ApplyPersistedRecord(persisted);
            }
            return;
        }

        if (existing is null)
        {
            ClipboardEvents.Add(new ClipboardHistoryItemRow(snapshot));
        }
        else
        {
            existing.UpdateLastCopiedAt(snapshot.CapturedAt);
        }
    }

    private async Task CaptureImageSnapshotAsync(ClipboardSnapshot snapshot)
    {
        await _imagePersistenceGate.WaitAsync();
        try
        {
            ClipboardImageRecord? persisted = null;
            var repository = _clipboardImageRepository;
            if (repository is not null && snapshot.ImagePayload is not null)
            {
                try
                {
                    persisted = await Task.Run(() => repository.Capture(
                        snapshot.ImagePayload, snapshot.Fingerprint, snapshot.CapturedAt));
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (ReferenceEquals(_clipboardImageRepository, repository))
                        {
                            _clipboardImageRepository = null;
                        }
                        StatusText.Text = "图片历史暂时无法写入本机，已切换为本次运行有效模式。";
                    });
                }
            }

            if (Dispatcher.HasShutdownStarted)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyCapturedImage(snapshot, persisted);
                ClipboardEventsView.Refresh();
                UpdateClipboardState();
            });
        }
        finally
        {
            _imagePersistenceGate.Release();
        }
    }

    private void ApplyCapturedImage(ClipboardSnapshot snapshot, ClipboardImageRecord? persisted)
    {
        var existing = ClipboardEvents.FirstOrDefault(row =>
            !row.IsText && string.Equals(row.Snapshot.Fingerprint, snapshot.Fingerprint, StringComparison.Ordinal));
        if (persisted is not null)
        {
            existing = ClipboardEvents.FirstOrDefault(row => row.Id == persisted.Id) ?? existing;
            if (existing is null)
            {
                ClipboardEvents.Add(new ClipboardHistoryItemRow(persisted));
            }
            else if (existing.IsPersisted)
            {
                existing.ApplyPersistedImageRecord(persisted);
            }
            else
            {
                ClipboardEvents.Remove(existing);
                ClipboardEvents.Add(new ClipboardHistoryItemRow(persisted));
            }
            return;
        }

        if (existing is null)
        {
            ClipboardEvents.Add(new ClipboardHistoryItemRow(snapshot));
        }
        else
        {
            existing.UpdateLastCopiedAt(snapshot.CapturedAt);
        }
    }

    private void OnStatusChanged(object? sender, string status) => Dispatcher.Invoke(() => StatusText.Text = status);

    private bool FilterClipboardEvent(object item) =>
        item is ClipboardHistoryItemRow row && ClipboardSearchMatcher.Matches(row.KindLabel, row.SearchText, ClipboardSearchBox?.Text);

    private void ClipboardSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ClipboardEventsView is null)
        {
            return;
        }
        ClipboardEventsView.Refresh();
        UpdateClipboardState();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        ClipboardSearchBox.Clear();
        ClipboardSearchBox.Focus();
    }

    private void UpdateClipboardState()
    {
        if (SearchCountText is null || ClipboardEmptyState is null)
        {
            return;
        }

        var query = ClipboardSearchBox?.Text?.Trim() ?? string.Empty;
        var visibleCount = ClipboardEventsView?.Cast<object>().Count() ?? ClipboardEvents.Count;
        SearchCountText.Text = query.Length == 0 ? $"已捕获 {ClipboardEvents.Count} 条" : $"找到 {visibleCount} 条 / 共 {ClipboardEvents.Count} 条";
        ClipboardEmptyState.Text = query.Length > 0 && visibleCount == 0
            ? "没有找到匹配的文字记录。图片暂不支持文字搜索。"
            : "复制文字或图片后，会显示在这里。";
        ClipboardEmptyState.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InitializeTodoStorage()
    {
        var paths = new AppDataPaths();
        var startup = new TodoStorageBootstrapper().Start(paths.DatabasePath);
        _todoRepository = startup.Repository;

        foreach (var record in startup.Records)
        {
            var item = new TodoItemRow(record.Id, record.Text, record.IsCompleted, record.CreatedAt, record.UpdatedAt, record.CompletedAt);
            TodoItems.Add(item);
        }

        SetTodoPersistenceState(startup.IsAvailable ? "待办与完成历史已安全保存在本机" : "本机保存暂不可用，本次运行有效");
    }

    private void InitializeClipboardStorage()
    {
        var paths = new AppDataPaths();
        var textStartup = new ClipboardTextStorageBootstrapper().Start(paths.DatabasePath);
        _clipboardTextRepository = textStartup.Repository;
        foreach (var record in textStartup.Records)
        {
            ClipboardEvents.Add(new ClipboardHistoryItemRow(record));
        }

        var imageStartup = new ClipboardImageStorageBootstrapper().Start(paths.DatabasePath, paths.ImageDirectory);
        _clipboardImageRepository = imageStartup.Repository;
        foreach (var record in imageStartup.Records)
        {
            ClipboardEvents.Add(new ClipboardHistoryItemRow(record));
        }

        ClipboardEventsView.Refresh();
        ClipboardPersistenceText.Text = textStartup.IsAvailable && imageStartup.IsAvailable
            ? "文字和图片历史保存在本机；图片已生成轻量缩略图"
            : "部分剪贴板历史本机保存暂不可用，相关操作仅本次运行有效";
    }

    private void SetTodoPersistenceState(string message)
    {
        if (TodoPersistenceText is not null)
        {
            TodoPersistenceText.Text = message;
        }
    }

    private bool TryPersistTodo(Action<TodoRepository> operation)
    {
        if (_todoRepository is null)
        {
            return false;
        }

        try
        {
            operation(_todoRepository);
            SetTodoPersistenceState("待办与完成历史已安全保存在本机");
            return true;
        }
        catch
        {
            SetTodoPersistenceState("本机保存发生错误，请稍后重试");
            return false;
        }
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodoFromInput();

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        AddTodoFromInput();
        e.Handled = true;
    }

    private void AddTodoFromInput()
    {
        var normalized = TodoRules.Normalize(TodoInput.Text);
        if (normalized is null)
        {
            StatusText.Text = "请输入待办内容后再添加。";
            TodoInput.Focus();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var item = new TodoItemRow(normalized, now);
        var persistenceConfigured = _todoRepository is not null;
        var persisted = TryPersistTodo(repository => repository.Add(item.ToRecord()));

        TodoItems.Add(item);
        TodoInput.Clear();
        RefreshTodoViews();
        TodoSectionTabs.SelectedIndex = 0;
        StatusText.Text = persisted ? "待办已添加并保存在本机。" : persistenceConfigured ? "待办已添加，但本机保存失败；本次运行仍可使用。" : "待办已添加（本次运行有效）。";
        TodoInput.Focus();
    }

    private void EditTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoItemRow item || item.IsCompleted)
        {
            return;
        }

        foreach (var todo in TodoItems.Where(todo => todo.IsEditing && todo != item))
        {
            todo.EditText = todo.Text;
            todo.IsEditing = false;
        }
        item.EditText = item.Text;
        item.IsEditing = true;
    }

    private void SaveTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodoItemRow item)
        {
            SaveTodoEdit(item);
        }
    }

    private void CancelTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoItemRow item)
        {
            return;
        }
        item.EditText = item.Text;
        item.IsEditing = false;
        StatusText.Text = "已取消编辑。";
    }

    private void TodoEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoItemRow item)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            SaveTodoEdit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.EditText = item.Text;
            item.IsEditing = false;
            StatusText.Text = "已取消编辑。";
            e.Handled = true;
        }
    }

    private void SaveTodoEdit(TodoItemRow item)
    {
        if (item.IsCompleted)
        {
            StatusText.Text = "完成记录不能直接编辑，请先恢复为未完成。";
            return;
        }

        var normalized = TodoRules.Normalize(item.EditText);
        if (normalized is null)
        {
            StatusText.Text = "待办内容不能为空。";
            return;
        }

        var updatedAt = DateTimeOffset.UtcNow;
        if (_todoRepository is not null && !TryPersistTodo(repository => repository.UpdateText(item.Id, normalized, updatedAt)))
        {
            StatusText.Text = "待办更新未保存，请重试。";
            return;
        }

        item.Text = normalized;
        item.EditText = normalized;
        item.IsEditing = false;
        item.MarkUpdated(updatedAt);
        StatusText.Text = _todoRepository is null ? "待办已更新（本次运行有效）。" : "待办已更新并保存。";
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoItemRow item)
        {
            return;
        }
        if (_todoRepository is not null && !TryPersistTodo(repository => repository.Delete(item.Id)))
        {
            StatusText.Text = "删除未保存，记录已保留，请重试。";
            return;
        }

        TodoItems.Remove(item);
        RefreshTodoViews();
        StatusText.Text = _todoRepository is null ? "记录已删除（本次运行有效）。" : "记录已删除并同步到本机数据。";
    }

    private void TodoCompletion_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTodoCompletionChange || (sender as FrameworkElement)?.DataContext is not TodoItemRow item)
        {
            return;
        }
        ChangeTodoCompletion(item, item.IsCompleted, stateAlreadyChanged: true);
    }

    private void RestoreTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodoItemRow item && item.IsCompleted)
        {
            ChangeTodoCompletion(item, false, stateAlreadyChanged: false);
        }
    }

    private void ChangeTodoCompletion(TodoItemRow item, bool desiredState, bool stateAlreadyChanged)
    {
        var previousState = stateAlreadyChanged ? !desiredState : item.IsCompleted;
        var previousCompletedAt = item.CompletedAt;
        var previousUpdatedAt = item.UpdatedAt;
        var updatedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? completedAt = desiredState ? updatedAt : null;

        if (_todoRepository is not null && !TryPersistTodo(repository => repository.UpdateCompletion(item.Id, desiredState, updatedAt, completedAt)))
        {
            if (stateAlreadyChanged)
            {
                try
                {
                    _suppressTodoCompletionChange = true;
                    item.MarkCompletion(previousState, previousCompletedAt, previousUpdatedAt);
                }
                finally
                {
                    _suppressTodoCompletionChange = false;
                }
            }
            RefreshTodoViews();
            StatusText.Text = "完成状态未保存，已恢复原状态。";
            return;
        }

        try
        {
            _suppressTodoCompletionChange = true;
            item.IsEditing = false;
            item.MarkCompletion(desiredState, completedAt, updatedAt);
        }
        finally
        {
            _suppressTodoCompletionChange = false;
        }
        RefreshTodoViews();
        TodoSectionTabs.SelectedIndex = desiredState ? 1 : 0;
        var action = desiredState ? $"待办已完成，时间为{updatedAt.ToLocalTime():HH:mm}" : "待办已恢复到今日待办";
        StatusText.Text = _todoRepository is null ? $"{action}（本次运行有效）。" : $"{action}并保存。";
    }

    private void RefreshTodoViews()
    {
        CurrentTodoItemsView?.Refresh();
        CompletedTodoItemsView?.Refresh();
        UpdateTodoState();
    }

    private void UpdateTodoState()
    {
        if (CurrentTodoEmptyState is null || CompletedTodoEmptyState is null)
        {
            return;
        }
        var currentCount = TodoItems.Count(item => !item.IsCompleted);
        var completedCount = TodoItems.Count(item => item.IsCompleted);
        CurrentTodoEmptyState.Visibility = currentCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedTodoEmptyState.Visibility = completedCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HeaderDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindVisualParent<Button>(e.OriginalSource as DependencyObject) is not null || FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }
        try
        {
            DragMove();
            NormalizeExpandedBounds(save: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void CollapsedDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindVisualParent<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }
        try
        {
            DragMove();
            ClampCollapsedToVisibleArea();
            _expandedBounds = new Rect(Left, Top, _expandedBounds.Width, _expandedBounds.Height);
            SaveExpandedBounds();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => SetExpanded(true);
    private void Collapse_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        if (!expanded)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            NormalizeExpandedBounds(save: true);
            _expanded = false;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedPanel.Visibility = Visibility.Visible;
            ResizeMode = ResizeMode.NoResize;
            SetResizeBorder(0);
            MinWidth = 0;
            MinHeight = 0;
            MaxWidth = WindowPlacementService.CollapsedWidth;
            MaxHeight = WindowPlacementService.CollapsedHeight;
            MinWidth = WindowPlacementService.CollapsedWidth;
            MinHeight = WindowPlacementService.CollapsedHeight;

            var display = _placementService.FindDisplayForBounds(_expandedBounds, _displayService.GetDisplays());
            ApplyRawBounds(_placementService.ClampCollapsed(new Point(_expandedBounds.Left, _expandedBounds.Top), display.WorkArea));
            return;
        }

        var location = new Point(Left, Top);
        _expanded = true;
        CollapsedPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        ResizeMode = ResizeMode.CanResize;
        SetResizeBorder(6);

        var candidate = new Rect(location.X, location.Y, _expandedBounds.Width, _expandedBounds.Height);
        var displays = _displayService.GetDisplays();
        var displayForCandidate = _placementService.FindDisplayForBounds(candidate, displays);
        _expandedBounds = _placementService.ClampExpanded(candidate, displayForCandidate.WorkArea);
        ApplyExpandedBounds(_expandedBounds, displays);
        SaveExpandedBounds();
    }

    private void ResetWindow_Click(object sender, RoutedEventArgs e)
    {
        if (!_expanded)
        {
            _expanded = true;
            CollapsedPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            ResizeMode = ResizeMode.CanResize;
            SetResizeBorder(6);
        }

        var displays = _displayService.GetDisplays();
        var primary = displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0];
        _expandedBounds = _placementService.CreateDefault(primary.WorkArea);
        ApplyExpandedBounds(_expandedBounds, displays);
        SaveExpandedBounds();
        StatusText.Text = "窗口已恢复默认位置和大小。";
    }

    private async void CopyClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipboardHistoryItemRow item || sender is not Button button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            var snapshotToCopy = item.Snapshot;
            if (!item.IsText && item.IsPersisted && _clipboardImageRepository is not null &&
                item.OriginalImageFileName is not null)
            {
                var original = _clipboardImageRepository.TryLoadOriginal(item.OriginalImageFileName);
                if (original is null)
                {
                    StatusText.Text = "原始图片暂时无法读取，未修改系统剪贴板。";
                    return;
                }
                snapshotToCopy = snapshotToCopy with { ImagePayload = original };
            }

            var copied = await _clipboardMonitor.CopyBackAsync(snapshotToCopy);
            if (!copied)
            {
                StatusText.Text = "重新复制失败，请稍后重试。";
                return;
            }

            var copiedAt = DateTimeOffset.UtcNow;
            if (item.IsText && item.IsPersisted && _clipboardTextRepository is not null)
            {
                try
                {
                    item.ApplyPersistedRecord(_clipboardTextRepository.Touch(item.Id, copiedAt));
                }
                catch
                {
                    StatusText.Text = "内容已复制，但最近复制时间暂时无法保存。";
                    return;
                }
            }
            else if (!item.IsText && item.IsPersisted && _clipboardImageRepository is not null)
            {
                try
                {
                    item.ApplyPersistedImageRecord(_clipboardImageRepository.UpdateLastCopiedAt(item.Id, copiedAt));
                }
                catch
                {
                    StatusText.Text = "图片已复制，但最近复制时间暂时无法保存。";
                    return;
                }
            }
            else
            {
                item.UpdateLastCopiedAt(copiedAt);
            }
            ClipboardEventsView.Refresh();
            UpdateClipboardState();
            StatusText.Text = item.IsText ? "文字已重新复制，并更新最近复制时间。" : "原始图片已重新复制，并更新最近复制时间。";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ToggleClipboardPin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipboardHistoryItemRow item)
        {
            return;
        }

        var desiredState = !item.IsPinned;
        try
        {
            if (item.IsText && item.IsPersisted && _clipboardTextRepository is not null)
            {
                item.ApplyPersistedRecord(_clipboardTextRepository.SetPinned(item.Id, desiredState));
            }
            else if (!item.IsText && item.IsPersisted && _clipboardImageRepository is not null)
            {
                item.ApplyPersistedImageRecord(_clipboardImageRepository.SetPinned(item.Id, desiredState));
            }
            else
            {
                item.SetPinned(desiredState);
            }
        }
        catch
        {
            StatusText.Text = desiredState ? "置顶未能保存，记录保持原位置。" : "取消置顶未能保存，记录保持原位置。";
            return;
        }

        ClipboardEventsView.Refresh();
        UpdateClipboardState();
        var kind = item.IsText ? "文字" : "图片";
        var action = desiredState ? "已置顶" : "已取消置顶";
        var persisted = item.IsPersisted && (item.IsText ? _clipboardTextRepository is not null : _clipboardImageRepository is not null);
        StatusText.Text = persisted
            ? $"{kind}记录{action}并保存在本机。"
            : $"{kind}记录{action}（本次运行有效）。";
    }

    private async void DeleteClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ClipboardHistoryItemRow item)
        {
            return;
        }

        if (item.IsText)
        {
            var persistedText = item.IsPersisted && _clipboardTextRepository is not null;
            if (persistedText)
            {
                try
                {
                    _clipboardTextRepository!.Delete(item.Id);
                }
                catch
                {
                    StatusText.Text = "文字记录未能删除，已保留原卡片。";
                    return;
                }
            }

            ClipboardEvents.Remove(item);
            ClipboardEventsView.Refresh();
            UpdateClipboardState();
            StatusText.Text = persistedText ? "文字记录已从本机删除。" : "文字记录已删除（本次运行有效）。";
            return;
        }

        var persistedImage = item.IsPersisted && _clipboardImageRepository is not null;
        ClipboardImageDeleteResult? deleteResult = null;
        if (persistedImage)
        {
            button.IsEnabled = false;
            await _imagePersistenceGate.WaitAsync();
            try
            {
                var repository = _clipboardImageRepository!;
                deleteResult = await Task.Run(() => repository.Delete(item.Id));
            }
            catch
            {
                StatusText.Text = "图片记录或文件未能安全删除，已保留原卡片。";
                return;
            }
            finally
            {
                _imagePersistenceGate.Release();
                button.IsEnabled = true;
            }
        }

        ClipboardEvents.Remove(item);
        ClipboardEventsView.Refresh();
        UpdateClipboardState();
        if (!persistedImage)
        {
            StatusText.Text = "图片记录已删除（本次运行有效）。";
        }
        else if (deleteResult?.FileCleanupCompleted == true)
        {
            StatusText.Text = "图片记录、原图和缩略图已从本机删除。";
        }
        else
        {
            StatusText.Text = "图片记录已删除；少量临时文件将在下次启动时继续安全清理。";
        }
    }
    private void EditClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipboardHistoryItemRow item || !item.IsText)
        {
            return;
        }
        foreach (var row in ClipboardEvents.Where(row => row.IsEditing && row != item))
        {
            row.ResetEditText();
            row.IsEditing = false;
        }
        item.ResetEditText();
        item.IsEditing = true;
        StatusText.Text = "正在编辑这条文字记录；保存后可单独重新复制。";
    }

    private void SaveClipboardEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ClipboardHistoryItemRow item)
        {
            SaveClipboardEdit(item);
        }
    }

    private void CancelClipboardEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipboardHistoryItemRow item)
        {
            return;
        }
        item.ResetEditText();
        item.IsEditing = false;
        StatusText.Text = "已取消剪贴板文字编辑。";
    }

    private void ClipboardEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipboardHistoryItemRow item)
        {
            return;
        }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveClipboardEdit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.ResetEditText();
            item.IsEditing = false;
            StatusText.Text = "已取消剪贴板文字编辑。";
            e.Handled = true;
        }
    }

    private void SaveClipboardEdit(ClipboardHistoryItemRow item)
    {
        if (!ClipboardEditRules.IsValidText(item.EditText))
        {
            StatusText.Text = "剪贴板文字不能为空。";
            return;
        }

        var editedText = item.EditText;
        var editedFingerprint = ClipboardFingerprint.ForText(editedText);
        if (item.IsPersisted && _clipboardTextRepository is not null)
        {
            try
            {
                var result = _clipboardTextRepository.Edit(item.Id, editedText, editedFingerprint);
                if (result.WasMerged)
                {
                    var target = ClipboardEvents.FirstOrDefault(row => row.Id == result.Record.Id);
                    if (target is null)
                    {
                        target = new ClipboardHistoryItemRow(result.Record);
                        ClipboardEvents.Add(target);
                    }
                    else
                    {
                        target.ApplyPersistedRecord(result.Record);
                    }
                    ClipboardEvents.Remove(item);
                    target.IsEditing = false;
                    StatusText.Text = "编辑内容与已有记录相同，已自动合并。";
                }
                else
                {
                    item.ApplyPersistedRecord(result.Record);
                    item.IsEditing = false;
                    StatusText.Text = "文字记录已更新并保存在本机。";
                }
            }
            catch
            {
                item.ResetEditText();
                StatusText.Text = "文字修改未能保存，已恢复原内容。";
                return;
            }
        }
        else
        {
            var duplicate = ClipboardEvents.FirstOrDefault(row => row != item && row.IsText &&
                string.Equals(row.Snapshot.Fingerprint, editedFingerprint, StringComparison.Ordinal));
            if (duplicate is not null)
            {
                if (item.LastCopiedAt > duplicate.LastCopiedAt)
                {
                    duplicate.UpdateLastCopiedAt(item.LastCopiedAt);
                }
                if (item.IsPinned && !duplicate.IsPinned)
                {
                    duplicate.SetPinned(true);
                }
                ClipboardEvents.Remove(item);
                StatusText.Text = "编辑内容与已有记录相同，已自动合并（本次运行有效）。";
            }
            else
            {
                item.ApplyTextEdit(editedText);
                item.IsEditing = false;
                StatusText.Text = "文字记录已更新（本次运行有效）。";
            }
        }
        ClipboardEventsView.Refresh();
        UpdateClipboardState();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnGeometryChanged(object? sender, EventArgs e)
    {
        if (!_initialized || _applyingGeometry || !_expanded)
        {
            return;
        }
        _expandedBounds = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        UpdateResizeLimits(_placementService.FindDisplayForBounds(_expandedBounds, _displayService.GetDisplays()).WorkArea);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        NormalizeExpandedBounds(save: true);
    }

    private void NormalizeExpandedBounds(bool save)
    {
        if (!_expanded)
        {
            return;
        }
        var displays = _displayService.GetDisplays();
        var current = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        var display = _placementService.FindDisplayForBounds(current, displays);
        _expandedBounds = _placementService.ClampExpanded(current, display.WorkArea);
        ApplyExpandedBounds(_expandedBounds, displays);
        if (save)
        {
            SaveExpandedBounds();
        }
    }

    private void ApplyExpandedBounds(Rect bounds, IReadOnlyList<DisplayWorkArea> displays)
    {
        var display = _placementService.FindDisplayForBounds(bounds, displays);
        UpdateResizeLimits(display.WorkArea);
        ApplyRawBounds(bounds);
    }

    private void ApplyRawBounds(Rect bounds)
    {
        _applyingGeometry = true;
        try
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        finally
        {
            _applyingGeometry = false;
        }
    }

    private void UpdateResizeLimits(Rect workArea)
    {
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        MinWidth = Math.Min(WindowPlacementService.MinimumWidth, workArea.Width);
        MinHeight = Math.Min(WindowPlacementService.MinimumHeight, workArea.Height);
    }

    private void ClampCollapsedToVisibleArea()
    {
        var displays = _displayService.GetDisplays();
        var current = new Rect(Left, Top, WindowPlacementService.CollapsedWidth, WindowPlacementService.CollapsedHeight);
        var display = _placementService.FindDisplayForBounds(current, displays);
        ApplyRawBounds(_placementService.ClampCollapsed(new Point(Left, Top), display.WorkArea));
    }

    private void SaveExpandedBounds()
    {
        if (!_initialized)
        {
            return;
        }
        var displays = _displayService.GetDisplays();
        var display = _placementService.FindDisplayForBounds(_expandedBounds, displays);
        var settings = new WindowPlacementSettings(_expandedBounds.Left, _expandedBounds.Top, _expandedBounds.Width, _expandedBounds.Height, display.DeviceName);
        if (!_settingsService.TrySave(settings))
        {
            StatusText.Text = "窗口位置暂时无法保存，但不影响继续使用。";
        }
    }

    private void SetResizeBorder(double thickness)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
        {
            chrome.ResizeBorderThickness = new Thickness(thickness);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_expanded)
            {
                NormalizeExpandedBounds(save: true);
            }
            else
            {
                ClampCollapsedToVisibleArea();
                _expandedBounds = new Rect(Left, Top, _expandedBounds.Width, _expandedBounds.Height);
                SaveExpandedBounds();
            }
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        if (_initialized)
        {
            if (_expanded)
            {
                NormalizeExpandedBounds(save: false);
            }
            else
            {
                _expandedBounds = new Rect(Left, Top, _expandedBounds.Width, _expandedBounds.Height);
            }
            SaveExpandedBounds();
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _clipboardMonitor.SnapshotCaptured -= OnSnapshotCaptured;
        _clipboardMonitor.StatusChanged -= OnStatusChanged;
        _clipboardMonitor.Dispose();
    }
}

