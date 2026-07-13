# 技术决策002：SQLite本地数据层

- 状态：已采用并扩展到数据库版本4
- 日期：2026-07-12
- 范围：待办、完成历史、文字剪贴板和图片剪贴板元数据本机持久化

## 背景

阶段0验证Windows窗口、剪贴板监听和基础交互；阶段1建立待办本机保存；阶段3补齐完成时间；阶段4完成文字历史闭环；阶段5A开始保存图片原图、缩略图和元数据，同时继续保证单条图片失败不影响待办和文字历史。

## 核心决策

1. 使用 `Microsoft.Data.Sqlite.Core 10.0.9` 访问SQLite；
2. Windows原生SQLite通过 `SQLitePCLRaw.bundle_winsqlite3 2.1.11` 提供；
3. 数据库路径固定为 `%LocalAppData%\HMinus\DesktopSpike\data\hminus.db`；
4. 图片文件目录固定为 `%LocalAppData%\HMinus\DesktopSpike\data\clipboard-images`；
5. 使用 `PRAGMA user_version` 管理架构，当前版本为4；
6. 初始化和版本迁移在事务中执行，可重复初始化；遇到高于程序支持的版本时拒绝改写；
7. 所有写入使用参数化SQL，时间统一以UTC ISO 8601保存；
8. 数据库损坏、被占用或不可写时不自动删除、不覆盖、不重建原文件，相关模块回退为本次运行有效模式；
9. 技术日志不记录待办正文、完整文字、密码、验证码、密钥、图片内容或图片文件副本。

## 版本1：待办基础表

```sql
CREATE TABLE todos (
    id TEXT PRIMARY KEY NOT NULL,
    text TEXT NOT NULL,
    is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
```

## 版本2：待办完成时间

版本2为 `todos` 增加可为空的 `completed_at_utc`：

- 勾选完成时写入准确完成时间；
- 恢复未完成时清空；
- 旧版已完成记录以 `updated_at_utc` 安全补全；
- 旧版未完成记录保持为空。

## 版本3：文字剪贴板表

```sql
CREATE TABLE clipboard_texts (
    id TEXT PRIMARY KEY,
    text TEXT NOT NULL,
    fingerprint TEXT NOT NULL UNIQUE,
    created_at_utc TEXT NOT NULL,
    last_copied_at_utc TEXT NOT NULL,
    is_pinned INTEGER NOT NULL DEFAULT 0
);
```

- 完整文字以SHA-256指纹建立唯一约束；
- 重复复制只更新最近复制时间；
- 编辑成已有内容时在事务中自动合并；
- 阶段4B启用文字置顶、取消置顶和单条删除。

## 版本4：图片剪贴板元数据表

```sql
CREATE TABLE clipboard_images (
    id TEXT PRIMARY KEY NOT NULL,
    fingerprint TEXT NOT NULL UNIQUE,
    original_file_name TEXT NOT NULL,
    thumbnail_file_name TEXT NOT NULL,
    pixel_width INTEGER NOT NULL CHECK (pixel_width > 0),
    pixel_height INTEGER NOT NULL CHECK (pixel_height > 0),
    created_at_utc TEXT NOT NULL,
    last_copied_at_utc TEXT NOT NULL,
    is_pinned INTEGER NOT NULL DEFAULT 0 CHECK (is_pinned IN (0, 1))
);
```

字段与文件规则：

- 图片内容使用SHA-256指纹去重；相同图片只更新时间，不创建第二组文件；
- 数据库只保存受管文件名，不保存应用目录之外的绝对路径；
- 原图统一编码为PNG；缩略图统一编码为PNG，最长边不超过320像素；
- 单图像素上限为6000万，PNG编码后上限为80MB，超过时安全拒绝；
- 文件先写临时文件，再移动为正式文件；数据库插入失败时删除本次新建的原图和缩略图；
- 启动读取缩略图失败时只标记该卡片异常，不阻断其他记录；
- 重新复制时按需读取原图，不把所有原图长期加载到内存；
- `is_pinned`从阶段5B起正式用于图片置顶状态，重复复制只更新时间并保留原置顶状态。

## 写入与界面一致性

- 待办完成或恢复先写数据库，再移动列表；
- 文字捕获、编辑、置顶和删除均以数据库成功为界面提交条件；
- 图片捕获先生成受管文件，再登记数据库；登记失败会回收本次文件；
- 图片重复捕获只更新时间并移动现有卡片；
- 图片重新复制成功后才更新最近复制时间；
- 原图不可读取时不得使用缩略图冒充原图写入系统剪贴板。
- 图片置顶以数据库更新成功作为界面提交条件；
- 图片删除先把受管文件移动到暂存名，再在事务中删除数据库记录；数据库失败时恢复文件并保留卡片；
- 启动时根据数据库引用恢复中断删除中的有效文件，并清理已无数据库记录的暂存文件。

## 迁移与失败保护

- 支持新建数据库直接初始化到版本4；
- 支持v1、v2、v3连续迁移至v4；
- v3→v4只新增图片表，不修改待办和文字表；
- 迁移失败时事务回滚，不删除或覆盖原数据库；
- 待办、文字和图片存储分别启动，单个模块失败不应阻止其他可用模块；
- `app-settings.json`仍只保存显示名称和隐私确认状态，不保存剪贴板正文或图片。

## 当前暂不处理

- 图片异常诊断、缺失文件修复界面、孤立文件自动回收和正式磁盘占用诊断；
- 1天、2天、4天自动清理和清空未置顶记录（阶段6）；
- 数据加密、备份、导入导出和正式数据库恢复界面。

## 验证要求

- 新库初始化到版本4且重复初始化幂等；
- v1、v2迁移到v4不丢待办，v3迁移只新增图片表；
- 文字历史全部阶段4能力继续通过；
- 图片原图和缩略图均写在受管目录；
- 图片元数据、缩略图和尺寸可重启恢复；
- 重复图片合并并更新时间；
- 逐条重新复制读取原图并保持原尺寸；
- 图片置顶和取消置顶重启后保持，置顶记录优先排序；
- 单条删除同步移除数据库记录、原图和缩略图，删除中断后能够安全恢复；
- 数据库插入失败时不留下本次不完整文件；
- 损坏数据库不被替换，数据库不可用时安全回退；
- 设置文件和技术日志中不出现剪贴板正文或图片内容。

## v1.0.0封板决定（2026-07-12）

- 正式版继续使用SQLite v4，不因安装包或产品名称变化修改数据库结构；
- 安装、覆盖安装和默认卸载均不得删除或迁移现有数据库与受管图片；
- 软件内自定义名称不参与数据库路径、表名、安装AppId或升级识别；
- 保存期限与自动清理延期到后续版本，v1.0.0不创建未使用的清理配置字段。
