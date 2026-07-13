using DesktopSpike.Models;
using DesktopSpike.Services;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var failures = new List<string>();

void Check(string name, bool condition)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        Console.Error.WriteLine($"FAIL  {name}");
        failures.Add(name);
    }
}

var preview = ClipboardPreviewFormatter.ForText("  第一行\r\n   第二行   ");
Check("文字预览折叠空白与换行", preview == "第一行 第二行");
Check("空白文字被格式化为空", ClipboardPreviewFormatter.ForText(" \r\n ").Length == 0);
Check("长文字预览被截断", ClipboardPreviewFormatter.ForText(new string('甲', 200)).Length == 161);

var hashA = ClipboardFingerprint.ForText("alpha");
var hashA2 = ClipboardFingerprint.ForText("alpha");
var hashB = ClipboardFingerprint.ForText("beta");
Check("文字指纹稳定", hashA == hashA2);
Check("不同文字指纹不同", hashA != hashB);

var deduplicator = new ClipboardEventDeduplicator();
var now = new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.FromHours(8));
Check("首次内容判定为新事件", deduplicator.Observe("A", now) == ClipboardObservation.New);
Check("连续相同内容判定为重复", deduplicator.Observe("A", now.AddMilliseconds(10)) == ClipboardObservation.Duplicate);
deduplicator.MarkSelfWrite("A", now.AddSeconds(1));
Check("自身写入回流被抑制", deduplicator.Observe("A", now.AddSeconds(1.1)) == ClipboardObservation.SelfWrite);
Check("同次自身写入的多个消息持续被抑制", deduplicator.Observe("A", now.AddSeconds(1.2)) == ClipboardObservation.SelfWrite);
deduplicator.MarkSelfWrite("B", now.AddSeconds(2));
Check("超时的自身写入标记不误抑制", deduplicator.Observe("B", now.AddSeconds(5)) == ClipboardObservation.New);

Check("空搜索显示文字记录", ClipboardSearchMatcher.Matches("文字", "Hello World", ""));
Check("空搜索显示图片记录", ClipboardSearchMatcher.Matches("图片", "图片 100×100", "   "));
Check("文字搜索忽略英文大小写", ClipboardSearchMatcher.Matches("文字", "Hello World", "hello"));
Check("文字搜索支持中文包含匹配", ClipboardSearchMatcher.Matches("文字", "今天完成需求文档", "需求"));
Check("有搜索词时不匹配图片", !ClipboardSearchMatcher.Matches("图片", "图片 100×100", "图片"));
Check("无匹配文字返回假", !ClipboardSearchMatcher.Matches("文字", "第一条", "第二条"));

var historySnapshot = new ClipboardSnapshot(
    ClipboardContentKind.Text,
    now,
    "原文字",
    ClipboardFingerprint.ForText("原文字"),
    TextPayload: "原文字");
var historyRow = new ClipboardHistoryItemRow(historySnapshot);
Check("文字历史记录保留自己的完整载荷", historyRow.Snapshot.TextPayload == "原文字" && historyRow.IsText);
var editedClipboardText = "  编辑后的第一行\n第二行  ";
historyRow.ApplyTextEdit(editedClipboardText);
Check("历史文字编辑后保留精确空格和换行", historyRow.Snapshot.TextPayload == editedClipboardText);
Check("历史文字编辑后更新摘要和指纹", historyRow.Summary == "编辑后的第一行 第二行" && historyRow.Snapshot.Fingerprint == ClipboardFingerprint.ForText(editedClipboardText));
Check("历史文字搜索使用完整内容而非截断摘要", ClipboardSearchMatcher.Matches(historyRow.KindLabel, historyRow.SearchText, "第二行"));
var imageHistoryRow = new ClipboardHistoryItemRow(new ClipboardSnapshot(ClipboardContentKind.Image, now, "图片 10 × 10", "image-fingerprint"));
Check("图片历史记录可识别为不可文字编辑", !imageHistoryRow.IsText);
Check("空白剪贴板编辑被拒绝", !ClipboardEditRules.IsValidText("  \r\n "));
Check("有效剪贴板编辑保留原始内容", ClipboardEditRules.IsValidText("  有效内容  "));

Check("待办输入去除首尾空格", TodoRules.Normalize("  写开发日志  ") == "写开发日志");
Check("空白待办被拒绝", TodoRules.Normalize(" \r\n ") is null);
Check("空待办被拒绝", TodoRules.Normalize(null) is null);

var todo = new TodoItemRow("测试待办", now);
var todoCompletionNotified = false;
todo.PropertyChanged += (_, args) => todoCompletionNotified |= args.PropertyName == nameof(TodoItemRow.IsCompleted);
todo.MarkCompletion(true, now, now);
Check("待办完成状态发出变更通知", todoCompletionNotified && todo.IsCompleted);
Check("待办完成时间可显示本地日期与时间", todo.CompletedAt == now && todo.CompletedDateLabel.Contains("2026年7月12日") && todo.CompletedTimeLabel == "13:00");
todo.MarkCompletion(false, null, now.AddMinutes(1));
Check("恢复未完成时清除完成时间", !todo.IsCompleted && todo.CompletedAt is null);

var placement = new WindowPlacementService();
var primary = new DisplayWorkArea("PRIMARY", new Rect(0, 0, 1920, 1040), true);
var secondary = new DisplayWorkArea("SECONDARY", new Rect(1920, 0, 1280, 900), false);
var displays = new[] { primary, secondary };
var defaultBounds = placement.CreateDefault(primary.WorkArea);
Check("默认宽度为330", defaultBounds.Width == WindowPlacementService.DefaultWidth);
Check("默认高度约为工作区三分之一", Math.Abs(defaultBounds.Height - primary.WorkArea.Height / 3) < 0.01);
Check("默认窗口位于主屏幕右下并留边距", defaultBounds.Right == primary.WorkArea.Right - WindowPlacementService.DefaultMargin && defaultBounds.Bottom == primary.WorkArea.Bottom - WindowPlacementService.DefaultMargin);

var tooSmall = placement.ClampExpanded(new Rect(-50, -50, 100, 100), primary.WorkArea);
Check("展开窗口执行最小尺寸限制", tooSmall.Width == WindowPlacementService.MinimumWidth && tooSmall.Height == WindowPlacementService.MinimumHeight);
Check("越界展开窗口被修正到工作区", tooSmall.Left == primary.WorkArea.Left && tooSmall.Top == primary.WorkArea.Top);
var tooLarge = placement.ClampExpanded(new Rect(0, 0, 4000, 3000), primary.WorkArea);
Check("展开窗口不超过工作区", tooLarge.Width == primary.WorkArea.Width && tooLarge.Height == primary.WorkArea.Height);

var collapsed = placement.ClampCollapsed(new Point(1900, 1020), primary.WorkArea);
Check("折叠贴纸尺寸正确", collapsed.Width == WindowPlacementService.CollapsedWidth && collapsed.Height == WindowPlacementService.CollapsedHeight);
Check("折叠贴纸保持在可见区域", collapsed.Right == primary.WorkArea.Right && collapsed.Bottom == primary.WorkArea.Bottom);

var savedOnSecondary = new WindowPlacementSettings(2100, 100, 400, 500, "SECONDARY");
var restoredSecondary = placement.Restore(savedOnSecondary, displays);
Check("按显示器名称恢复窗口", restoredSecondary.Left == 2100 && restoredSecondary.Top == 100);
var missingMonitor = new WindowPlacementSettings(5000, 5000, 400, 500, "MISSING");
var restoredPrimary = placement.Restore(missingMonitor, displays);
Check("显示器移除后回退主屏幕", primary.WorkArea.Contains(restoredPrimary));
var invalid = new WindowPlacementSettings(double.NaN, 0, 330, 400, "PRIMARY");
Check("异常设置回退默认位置", placement.Restore(invalid, displays) == defaultBounds);
Check("按窗口中心选择当前显示器", placement.FindDisplayForBounds(new Rect(2200, 100, 330, 400), displays).DeviceName == "SECONDARY");

var tempDirectory = Path.Combine(Path.GetTempPath(), "HMinus-WindowSettings-" + Guid.NewGuid().ToString("N"));
try
{
    var settingsService = new WindowSettingsService(tempDirectory);
    Check("窗口设置可安全写入", settingsService.TrySave(savedOnSecondary));
    Check("窗口设置可往返读取", settingsService.Load() == savedOnSecondary);
    var settingsJson = File.ReadAllText(settingsService.SettingsPath);
    Check("设置文件只包含窗口几何字段", !settingsJson.Contains("clipboard", StringComparison.OrdinalIgnoreCase) && !settingsJson.Contains("content", StringComparison.OrdinalIgnoreCase));
    File.WriteAllText(settingsService.SettingsPath, "{broken-json");
    Check("损坏设置文件安全回退", settingsService.Load() is null);
}
finally
{
    if (Directory.Exists(tempDirectory))
    {
        Directory.Delete(tempDirectory, true);
    }
}



var appSettingsDirectory = Path.Combine(Path.GetTempPath(), "HMinus-AppSettings-" + Guid.NewGuid().ToString("N"));
try
{
    var appSettingsService = new AppSettingsService(appSettingsDirectory);
    Check("名称去除首尾空格", AppNameRules.Normalize("  我的效率屋  ") == "我的效率屋");
    Check("空名称被拒绝", AppNameRules.Normalize("   ") is null);
    Check("超过24字符的名称被拒绝", AppNameRules.Normalize(new string('名', 25)) is null);
    Check("应用设置缺失时使用默认名称", appSettingsService.Load().DisplayName == AppNameRules.DefaultDisplayName);
    Check("自定义名称和隐私确认可安全写入", appSettingsService.TrySave(new AppSettings("我的效率屋", true)));
    var loadedAppSettings = appSettingsService.Load();
    Check("自定义名称可往返读取", loadedAppSettings.DisplayName == "我的效率屋");
    Check("剪贴板隐私确认可往返读取", loadedAppSettings.ClipboardPrivacyNoticeAccepted);
    var appJson = File.ReadAllText(appSettingsService.SettingsPath);
    Check("应用设置不包含剪贴板正文", !appJson.Contains("验证码123456", StringComparison.OrdinalIgnoreCase) && !appJson.Contains("TextPayload", StringComparison.OrdinalIgnoreCase));
    File.WriteAllText(appSettingsService.SettingsPath, "{broken-json");
    Check("损坏应用设置安全回退默认名称", appSettingsService.Load().DisplayName == AppNameRules.DefaultDisplayName);
}
finally
{
    if (Directory.Exists(appSettingsDirectory))
    {
        Directory.Delete(appSettingsDirectory, true);
    }
}

var dataTempDirectory = Path.Combine(Path.GetTempPath(), "HMinus-Data-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new AppDataPaths(dataTempDirectory);
    Check("数据路径位于指定本机根目录", paths.DatabasePath.StartsWith(dataTempDirectory, StringComparison.OrdinalIgnoreCase));
    Check("图片目录位于应用数据目录", paths.ImageDirectory.StartsWith(paths.DataDirectory, StringComparison.OrdinalIgnoreCase));

    var initializer = new DatabaseInitializer();
    initializer.Initialize(paths.DatabasePath);
    using (var connection = DatabaseInitializer.OpenConnection(paths.DatabasePath))
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Check("数据库初始化到版本4", Convert.ToInt32(versionCommand.ExecuteScalar()) == 4);

        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT group_concat(name, ',') FROM sqlite_master WHERE type = 'table';";
        var tables = Convert.ToString(tableCommand.ExecuteScalar()) ?? string.Empty;
        Check("版本4包含待办、文字和图片剪贴板表", tables.Split(',').Contains("todos") && tables.Split(',').Contains("clipboard_texts") && tables.Split(',').Contains("clipboard_images"));
    }

    initializer.Initialize(paths.DatabasePath);
    Check("数据库重复初始化保持幂等", File.Exists(paths.DatabasePath));

    var repository = new TodoRepository(paths.DatabasePath);
    var createdEarlier = new DateTimeOffset(2026, 7, 12, 1, 0, 0, TimeSpan.Zero);
    var createdLater = createdEarlier.AddMinutes(10);
    var firstRecord = new TodoRecord(Guid.NewGuid(), "较早未完成", false, createdEarlier, createdEarlier, null);
    var secondRecord = new TodoRecord(Guid.NewGuid(), "较晚未完成", false, createdLater, createdLater, null);
    var completedAt = createdLater.AddMinutes(20);
    var completedRecord = new TodoRecord(Guid.NewGuid(), "已完成", true, createdLater.AddMinutes(10), completedAt, completedAt);
    repository.Add(firstRecord);
    repository.Add(secondRecord);
    repository.Add(completedRecord);

    var loaded = new TodoRepository(paths.DatabasePath).LoadAll();
    Check("新增待办重载后保留完整字段", loaded.Any(item => item == firstRecord));
    Check("待办按未完成优先且组内时间降序读取", loaded.Select(item => item.Id).SequenceEqual(new[] { secondRecord.Id, firstRecord.Id, completedRecord.Id }));

    var editedAt = createdLater.AddHours(1);
    repository.UpdateText(firstRecord.Id, "已编辑", editedAt);
    var newlyCompletedAt = editedAt.AddMinutes(1);
    repository.UpdateCompletion(firstRecord.Id, true, newlyCompletedAt, newlyCompletedAt);
    var edited = repository.LoadAll().Single(item => item.Id == firstRecord.Id);
    Check("待办编辑可持久化", edited.Text == "已编辑" && edited.UpdatedAt == editedAt.AddMinutes(1));
    Check("待办完成状态和完成时间可持久化", edited.IsCompleted && edited.CompletedAt == newlyCompletedAt);
    repository.UpdateCompletion(firstRecord.Id, false, newlyCompletedAt.AddMinutes(1), null);
    var restored = repository.LoadAll().Single(item => item.Id == firstRecord.Id);
    Check("恢复未完成会清除完成时间", !restored.IsCompleted && restored.CompletedAt is null);

    repository.Delete(firstRecord.Id);
    Check("待办删除可持久化", repository.LoadAll().All(item => item.Id != firstRecord.Id));

    var clipboardRepository = new ClipboardTextRepository(paths.DatabasePath);
    var copiedFirst = new DateTimeOffset(2026, 7, 12, 5, 0, 0, TimeSpan.Zero);
    var firstText = "第一条长期文字";
    var firstFingerprint = ClipboardFingerprint.ForText(firstText);
    var captured = clipboardRepository.Capture(firstText, firstFingerprint, copiedFirst);
    Check("新文字可持久化", clipboardRepository.LoadAll().Single().Text == firstText);
    var duplicateCapture = clipboardRepository.Capture(firstText, firstFingerprint, copiedFirst.AddMinutes(5));
    Check("重复文字合并并更新时间", duplicateCapture.Id == captured.Id && duplicateCapture.LastCopiedAt == copiedFirst.AddMinutes(5) && clipboardRepository.LoadAll().Count == 1);
    var secondText = "第二条长期文字";
    var secondFingerprint = ClipboardFingerprint.ForText(secondText);
    var second = clipboardRepository.Capture(secondText, secondFingerprint, copiedFirst.AddMinutes(2));
    Check("文字历史按最近复制时间降序", clipboardRepository.LoadAll().Select(item => item.Id).SequenceEqual(new[] { captured.Id, second.Id }));
    var pinned = clipboardRepository.SetPinned(second.Id, true);
    Check("文字置顶状态可持久化", pinned.IsPinned && clipboardRepository.LoadAll().Single(item => item.Id == second.Id).IsPinned);
    Check("置顶文字优先于较新的未置顶文字", clipboardRepository.LoadAll().First().Id == second.Id);
    var unpinned = clipboardRepository.SetPinned(second.Id, false);
    Check("文字可取消置顶", !unpinned.IsPinned && clipboardRepository.LoadAll().First().Id == captured.Id);
    clipboardRepository.SetPinned(second.Id, true);
    var duplicatePinned = clipboardRepository.Capture(secondText, secondFingerprint, copiedFirst.AddMinutes(6));
    Check("重复复制保留置顶状态", duplicatePinned.Id == second.Id && duplicatePinned.IsPinned);
    var touched = clipboardRepository.Touch(second.Id, copiedFirst.AddMinutes(8));
    Check("逐条重新复制可更新时间", touched.LastCopiedAt == copiedFirst.AddMinutes(8) && clipboardRepository.LoadAll().First().Id == second.Id);
    var editedText = "编辑后的完整文字";
    var editedClipboard = clipboardRepository.Edit(second.Id, editedText, ClipboardFingerprint.ForText(editedText));
    Check("文字编辑可持久化", !editedClipboard.WasMerged && clipboardRepository.LoadAll().Single(item => item.Id == second.Id).Text == editedText);
    var mergedClipboard = clipboardRepository.Edit(second.Id, firstText, firstFingerprint);
    Check("编辑成重复文字时自动合并", mergedClipboard.WasMerged && mergedClipboard.RemovedRecordId == second.Id && clipboardRepository.LoadAll().Count == 1 && clipboardRepository.LoadAll().Single().Id == captured.Id);
    Check("编辑合并保留任一记录的置顶状态", mergedClipboard.Record.IsPinned && clipboardRepository.LoadAll().Single().IsPinned);
    clipboardRepository.Delete(captured.Id);
    Check("文字记录可永久删除", clipboardRepository.LoadAll().Count == 0);

    var imageRepository = new ClipboardImageRepository(paths.DatabasePath, paths.ImageDirectory);
    var testImage = new WriteableBitmap(4, 3, 96, 96, PixelFormats.Bgra32, null);
    var pixels = Enumerable.Range(0, 4 * 3).SelectMany(index => new byte[] { (byte)(index * 5), 120, 220, 255 }).ToArray();
    testImage.WritePixels(new Int32Rect(0, 0, 4, 3), pixels, 4 * 4, 0);
    testImage.Freeze();
    var imageFingerprint = ClipboardFingerprint.ForImage(testImage);
    var imageCapturedAt = copiedFirst.AddHours(1);
    var savedImage = imageRepository.Capture(testImage, imageFingerprint, imageCapturedAt);
    Check("图片原图和缩略图安全写入应用目录",
        File.Exists(Path.Combine(paths.ImageDirectory, savedImage.OriginalFileName)) &&
        File.Exists(Path.Combine(paths.ImageDirectory, savedImage.ThumbnailFileName)));
    Check("图片元数据和缩略图可重启恢复",
        imageRepository.LoadAll().Single() is { PixelWidth: 4, PixelHeight: 3, Thumbnail: not null });
    var duplicateImage = imageRepository.Capture(testImage, imageFingerprint, imageCapturedAt.AddMinutes(3));
    Check("重复图片合并并更新时间",
        duplicateImage.Id == savedImage.Id && duplicateImage.LastCopiedAt == imageCapturedAt.AddMinutes(3) && imageRepository.LoadAll().Count == 1);
    var originalImage = imageRepository.TryLoadOriginal(duplicateImage);
    Check("持久化原图可重新读取用于复制", originalImage is { PixelWidth: 4, PixelHeight: 3 });
    var imageRow = new ClipboardHistoryItemRow(duplicateImage);
    Check("持久化图片卡片使用缩略图且保留原图引用",
        !imageRow.IsText && imageRow.IsPersisted && imageRow.HasImagePreview && imageRow.OriginalImageFileName == duplicateImage.OriginalFileName);
    var pinnedImage = imageRepository.SetPinned(savedImage.Id, true);
    Check("图片置顶状态可持久化", pinnedImage.IsPinned && imageRepository.LoadAll().Single().IsPinned);
    var duplicatePinnedImage = imageRepository.Capture(testImage, imageFingerprint, imageCapturedAt.AddMinutes(5));
    Check("重复复制图片保留置顶状态", duplicatePinnedImage.Id == savedImage.Id && duplicatePinnedImage.IsPinned);
    var unpinnedImage = imageRepository.SetPinned(savedImage.Id, false);
    Check("图片可取消置顶", !unpinnedImage.IsPinned && !imageRepository.LoadAll().Single().IsPinned);

    var fileStore = new ClipboardImageFileStore(paths.ImageDirectory);
    using (fileStore.StageDelete(new SavedImageFiles(savedImage.OriginalFileName, savedImage.ThumbnailFileName)))
    {
        var recoveredRepository = new ClipboardImageRepository(paths.DatabasePath, paths.ImageDirectory);
        var recovered = recoveredRepository.LoadAll().Single();
        Check("删除中断后重启可恢复数据库仍引用的图片文件",
            File.Exists(Path.Combine(paths.ImageDirectory, recovered.OriginalFileName)) &&
            File.Exists(Path.Combine(paths.ImageDirectory, recovered.ThumbnailFileName)));
    }

    var imageDelete = imageRepository.Delete(savedImage.Id);
    Check("图片记录可永久删除", imageRepository.LoadAll().Count == 0);
    Check("删除图片同步移除原图和缩略图", imageDelete.FileCleanupCompleted &&
        !File.Exists(Path.Combine(paths.ImageDirectory, savedImage.OriginalFileName)) &&
        !File.Exists(Path.Combine(paths.ImageDirectory, savedImage.ThumbnailFileName)));
}
finally
{
    if (Directory.Exists(dataTempDirectory))
    {
        Directory.Delete(dataTempDirectory, true);
    }
}


var migrationTempDirectory = Path.Combine(Path.GetTempPath(), "HMinus-Migration-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(migrationTempDirectory);
    var migrationPath = Path.Combine(migrationTempDirectory, "hminus.db");
    using (var connection = DatabaseInitializer.OpenConnection(migrationPath))
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE todos (
                id TEXT PRIMARY KEY NOT NULL,
                text TEXT NOT NULL,
                is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            INSERT INTO todos VALUES ('11111111-1111-1111-1111-111111111111', '旧版已完成', 1, '2026-07-12T01:00:00.0000000+00:00', '2026-07-12T02:00:00.0000000+00:00');
            INSERT INTO todos VALUES ('22222222-2222-2222-2222-222222222222', '旧版未完成', 0, '2026-07-12T03:00:00.0000000+00:00', '2026-07-12T03:00:00.0000000+00:00');
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    new DatabaseInitializer().Initialize(migrationPath);
    using (var connection = DatabaseInitializer.OpenConnection(migrationPath))
    {
        Check("版本1数据库可连续迁移到版本4", DatabaseInitializer.ReadSchemaVersion(connection) == 4);
    }
    var migrated = new TodoRepository(migrationPath).LoadAll();
    var migratedCompleted = migrated.Single(item => item.IsCompleted);
    var migratedCurrent = migrated.Single(item => !item.IsCompleted);
    Check("旧版已完成记录用更新时间补全完成时间", migratedCompleted.CompletedAt == migratedCompleted.UpdatedAt);
    Check("旧版未完成记录迁移后完成时间为空", migratedCurrent.CompletedAt is null);
}
finally
{
    if (Directory.Exists(migrationTempDirectory))
    {
        Directory.Delete(migrationTempDirectory, true);
    }
}

var v2MigrationDirectory = Path.Combine(Path.GetTempPath(), "HMinus-V2Migration-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(v2MigrationDirectory);
    var v2Path = Path.Combine(v2MigrationDirectory, "hminus.db");
    using (var connection = DatabaseInitializer.OpenConnection(v2Path))
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE todos (
                id TEXT PRIMARY KEY NOT NULL,
                text TEXT NOT NULL,
                is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL
            );
            INSERT INTO todos VALUES ('33333333-3333-3333-3333-333333333333', '版本2待办', 0, '2026-07-12T04:00:00.0000000+00:00', '2026-07-12T04:00:00.0000000+00:00', NULL);
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
    }
    new DatabaseInitializer().Initialize(v2Path);
    using (var connection = DatabaseInitializer.OpenConnection(v2Path))
    {
        Check("版本2数据库迁移到版本4", DatabaseInitializer.ReadSchemaVersion(connection) == 4);
    }
    Check("版本2待办迁移后不丢失", new TodoRepository(v2Path).LoadAll().Single().Text == "版本2待办");
    Check("版本2迁移后文字仓储可用", new ClipboardTextRepository(v2Path).LoadAll().Count == 0);
}
finally
{
    if (Directory.Exists(v2MigrationDirectory))
    {
        Directory.Delete(v2MigrationDirectory, true);
    }
}

var v3MigrationDirectory = Path.Combine(Path.GetTempPath(), "HMinus-V3Migration-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(v3MigrationDirectory);
    var v3Path = Path.Combine(v3MigrationDirectory, "hminus.db");
    using (var connection = DatabaseInitializer.OpenConnection(v3Path))
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE todos (
                id TEXT PRIMARY KEY NOT NULL,
                text TEXT NOT NULL,
                is_completed INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL
            );
            CREATE TABLE clipboard_texts (
                id TEXT PRIMARY KEY NOT NULL,
                text TEXT NOT NULL,
                fingerprint TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                last_copied_at_utc TEXT NOT NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO clipboard_texts VALUES
                ('44444444-4444-4444-4444-444444444444', '版本3文字', 'v3-fingerprint',
                 '2026-07-12T04:00:00.0000000+00:00', '2026-07-12T04:00:00.0000000+00:00', 1);
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
    }
    new DatabaseInitializer().Initialize(v3Path);
    using (var connection = DatabaseInitializer.OpenConnection(v3Path))
    {
        Check("版本3数据库迁移到版本4", DatabaseInitializer.ReadSchemaVersion(connection) == 4);
    }
    Check("版本3迁移后文字记录和置顶状态不丢失",
        new ClipboardTextRepository(v3Path).LoadAll().Single() is { Text: "版本3文字", IsPinned: true });
    Check("版本3迁移后图片仓储可用",
        new ClipboardImageRepository(v3Path, Path.Combine(v3MigrationDirectory, "images")).LoadAll().Count == 0);
}
finally
{
    if (Directory.Exists(v3MigrationDirectory))
    {
        Directory.Delete(v3MigrationDirectory, true);
    }
}

var corruptTempDirectory = Path.Combine(Path.GetTempPath(), "HMinus-Corrupt-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(corruptTempDirectory);
    var corruptPath = Path.Combine(corruptTempDirectory, "hminus.db");
    var corruptBytes = new byte[] { 1, 3, 3, 7, 9, 9, 2 };
    File.WriteAllBytes(corruptPath, corruptBytes);
    var corruptStartup = new TodoStorageBootstrapper().Start(corruptPath);
    Check("损坏数据库启动时安全回退到内存", !corruptStartup.IsAvailable && corruptStartup.Repository is null && corruptStartup.Records.Count == 0);
    Check("损坏数据库不会被静默删除或替换", File.ReadAllBytes(corruptPath).SequenceEqual(corruptBytes));

    var directoryAsDatabase = Path.Combine(corruptTempDirectory, "directory.db");
    Directory.CreateDirectory(directoryAsDatabase);
    var unavailableStartup = new TodoStorageBootstrapper().Start(directoryAsDatabase);
    Check("数据库不可用时应用数据层可继续以内存模式启动", !unavailableStartup.IsAvailable && unavailableStartup.Repository is null);
}
finally
{
    if (Directory.Exists(corruptTempDirectory))
    {
        Directory.Delete(corruptTempDirectory, true);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} verification(s) failed.");
    return 1;
}

Console.WriteLine("\nAll prototype verification checks passed.");
return 0;


