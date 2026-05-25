using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WinLink.App.Models;

/// <summary>
/// 标识当前任务或记录的源对象类型。
/// </summary>
public enum LinkSourceKind
{
    Unknown = 0,
    File = 1,
    Directory = 2,
}

/// <summary>
/// 表示目标链接在检查后的健康状态。
/// </summary>
public enum LinkTargetState
{
    Unknown = 0,
    Active = 1,
    Invalid = 2,
    Disconnected = 3,
}

/// <summary>
/// 表示链接的推荐或实际创建策略。
/// </summary>
public enum LinkCreationStrategy
{
    Auto = 0,
    SymbolicLink = 1,
    Junction = 2,
    HardLink = 3,
}

/// <summary>
/// 表示目标录入采用的工作流。
/// </summary>
public enum LinkTargetInputMode
{
    FullPath = 0,
    DirectoryWithName = 1,
}

/// <summary>
/// 受管链接注册表的顶层文档结构。
/// </summary>
public sealed class ManagedLinkRegistryDocument
{
    /// <summary>
    /// 当前应用使用的注册表 JSON 版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 当前文档的结构版本。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 所有受管源任务及其目标映射。
    /// </summary>
    public List<ManagedLinkRecord> Records { get; set; } = [];
}

/// <summary>
/// 表示一条由 WinLink 管理的源任务记录。
/// </summary>
public sealed class ManagedLinkRecord
{
    /// <summary>
    /// 记录唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 用户可见的任务名称或备注。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 源路径。
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 源对象类型。
    /// </summary>
    public LinkSourceKind SourceKind { get; set; }

    /// 如果当前任务复用了已连接源，则记录其原始受管记录标识。
    /// </summary>

    /// <summary>
    /// 如果当前任务复用了已连接源，则记录其原始受管记录标识。
    /// </summary>

    /// <summary>
    /// 首选链接策略。
    /// </summary>
    public LinkCreationStrategy PreferredStrategy { get; set; } = LinkCreationStrategy.Auto;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 最近更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 当前任务关联的目标列表。
    /// </summary>
    public List<ManagedLinkTargetRecord> Targets { get; set; } = [];

    /// <summary>
    /// 记录当前是否在 UI 中展开详情区，用于恢复上次查看状态。
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// 用于左侧列表展示的目标状态摘要。
    /// </summary>
    public string StatusSummary =>
        $"目标 {Targets.Count} / 生效 {Targets.Count(target => target.State == LinkTargetState.Active)} / 失效 {Targets.Count(target => target.State == LinkTargetState.Invalid)} / 已断开 {Targets.Count(target => target.State == LinkTargetState.Disconnected)}";
}

/// <summary>
/// 表示受管任务中的单个目标项记录。
/// </summary>
public sealed class ManagedLinkTargetRecord
{
    /// <summary>
    /// 目标项唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 用户可见的目标名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 目标路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 当前健康状态。
    /// </summary>
    public LinkTargetState State { get; set; } = LinkTargetState.Unknown;

    /// <summary>
    /// 最近一次状态说明或失败原因。
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// 实际采用的策略。
    /// </summary>
    public LinkCreationStrategy AppliedStrategy { get; set; } = LinkCreationStrategy.Auto;

    /// <summary>
    /// 最近一次状态检查时间。
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>
    /// 将底层状态映射成用户可读的主文本。
    /// </summary>
    public string StateDisplayName => State switch
    {
        LinkTargetState.Active => "生效中",
        LinkTargetState.Invalid => "失效",
        LinkTargetState.Disconnected => "已断开",
        _ => "未检查",
    };
}

/// <summary>
/// 执行历史文档的顶层结构。
/// </summary>
public sealed class ExecutionHistoryDocument
{
    /// <summary>
    /// 当前执行历史 JSON 版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 当前文档的结构版本。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 最近执行记录列表。
    /// </summary>
    public List<ExecutionHistoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// 表示一次校验或执行的历史摘要。
/// </summary>
public sealed class ExecutionHistoryEntry
{
    /// <summary>
    /// 历史记录唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 执行或校验发生的时间。
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 本次任务摘要。
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 成功数量。
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 跳过数量。
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 失败数量。
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public List<string> FailureReasons { get; set; } = [];
}

/// <summary>
/// 保存轻量 UI 状态的文档结构。
/// </summary>
public sealed class WorkspaceUiStateDocument
{
    /// <summary>
    /// 当前 UI 状态 JSON 版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 当前文档的结构版本。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 上次选中的待执行任务。
    /// </summary>
    public string? SelectedPendingTaskId { get; set; }

    /// <summary>
    /// 上次选中的受管源任务。
    /// </summary>
    public string? SelectedManagedLinkId { get; set; }

    /// <summary>
    /// 用户上次展开的受管源任务标识。
    /// </summary>
    public List<string> ExpandedManagedLinkIds { get; set; } = [];
}

/// <summary>
/// 表示当前会话中的一条待执行任务。
/// </summary>
public sealed class LinkTaskDraft : INotifyPropertyChanged
{
    private string displayName = string.Empty;
    private LinkCreationStrategy preferredStrategy = LinkCreationStrategy.Auto;
    private string? reusedManagedSourceRecordId;
    private LinkSourceKind sourceKind;
    private string sourcePath = string.Empty;

    /// <summary>
    /// 任务唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 任务备注名。
    /// </summary>
    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    /// <summary>
    /// 源路径。
    /// </summary>
    public string SourcePath
    {
        get => sourcePath;
        set => SetProperty(ref sourcePath, value);
    }

    /// <summary>
    /// 锁定后的源类型。
    /// </summary>
    public LinkSourceKind SourceKind
    {
        get => sourceKind;
        set
        {
            if (SetProperty(ref sourceKind, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSourceLocked)));
            }
        }
    }

    /// <summary>
    /// 当前任务是否已锁定源类型。
    /// </summary>
    public bool IsSourceLocked => SourceKind != LinkSourceKind.Unknown;

    /// <summary>
    /// 当前任务的手动高级策略选择；`Auto` 表示按系统推荐并在必要时提示降级。
    /// </summary>
    public LinkCreationStrategy PreferredStrategy
    {
        get => preferredStrategy;
        set => SetProperty(ref preferredStrategy, value);
    }

    /// <summary>
    /// 当前任务复用的已连接源记录标识；为空表示按普通新建源任务处理。
    /// </summary>
    public string? ReusedManagedSourceRecordId
    {
        get => reusedManagedSourceRecordId;
        set
        {
            if (SetProperty(ref reusedManagedSourceRecordId, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReusingManagedSource)));
            }
        }
    }

    /// <summary>
    /// 当前任务是否处于“复用已连接源”模式。
    /// </summary>
    public bool IsReusingManagedSource => !string.IsNullOrWhiteSpace(ReusedManagedSourceRecordId);

    /// <summary>
    /// 当前任务的目标集合。
    /// </summary>
    public ObservableCollection<LinkTargetDraft> Targets { get; set; } = [];

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<TValue>(ref TValue storage, TValue value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>
/// 表示批量执行前的统一规划结果。
/// </summary>
public sealed class LinkExecutionPlan
{
    /// <summary>
    /// 参与本次执行的目标计划列表。
    /// </summary>
    public List<PlannedLinkTarget> Targets { get; set; } = [];

    /// <summary>
    /// 需要在执行前统一确认的降级项。
    /// </summary>
    public List<LinkExecutionDowngradeItem> Downgrades { get; set; } = [];
}

/// <summary>
/// 表示单个目标在执行前的策略决策结果。
/// </summary>
public sealed class PlannedLinkTarget
{
    /// <summary>
    /// 所属任务标识。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 所属任务名称。
    /// </summary>
    public string TaskDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 源路径。
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 源类型。
    /// </summary>
    public LinkSourceKind SourceKind { get; set; }

    /// <summary>
    /// 如果当前任务复用了已连接源，则记录其原始受管记录标识。
    /// </summary>
    public string? ReusedManagedSourceRecordId { get; set; }

    /// <summary>
    /// 目标草稿引用。
    /// </summary>
    public LinkTargetDraft Target { get; set; } = new();

    /// <summary>
    /// 推荐策略。
    /// </summary>
    public LinkCreationStrategy RecommendedStrategy { get; set; }

    /// <summary>
    /// 计划采用的策略。
    /// </summary>
    public LinkCreationStrategy PlannedStrategy { get; set; }

    /// <summary>
    /// 当前目标是否需要从推荐策略降级。
    /// </summary>
    public bool RequiresDowngrade { get; set; }
}

/// <summary>
/// 表示一个需要统一确认的降级项。
/// </summary>
public sealed class LinkExecutionDowngradeItem
{
    /// <summary>
    /// 任务名称。
    /// </summary>
    public string TaskDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 目标名称。
    /// </summary>
    public string TargetDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 目标路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 推荐策略。
    /// </summary>
    public LinkCreationStrategy RecommendedStrategy { get; set; }

    /// <summary>
    /// 可接受的替代策略。
    /// </summary>
    public LinkCreationStrategy FallbackStrategy { get; set; }
}

/// <summary>
/// 表示一次批量执行完成后的聚合结果。
/// </summary>
public sealed class LinkExecutionBatchResult
{
    /// <summary>
    /// 已写入历史的执行摘要。
    /// </summary>
    public ExecutionHistoryEntry HistoryEntry { get; set; } = new();

    /// <summary>
    /// 本次成功写入注册表的受管记录。
    /// </summary>
    public List<ManagedLinkRecord> ManagedRecords { get; set; } = [];
}

/// <summary>
/// 表示待执行任务中的单个目标草稿。
/// </summary>
public sealed class LinkTargetDraft : INotifyPropertyChanged
{
    private string displayName = string.Empty;
    private LinkTargetInputMode inputMode = LinkTargetInputMode.FullPath;
    private bool suppressDisplayNamePathSync;
    private string targetPath = string.Empty;
    private string? targetDirectoryPath;
    private string? validationMessage;

    /// <summary>
    /// 目标草稿唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 目标显示名。
    /// </summary>
    public string DisplayName
    {
        get => displayName;
        set
        {
            if (!SetProperty(ref displayName, value))
            {
                return;
            }

            if (suppressDisplayNamePathSync)
            {
                return;
            }

            if (InputMode == LinkTargetInputMode.DirectoryWithName && !string.IsNullOrWhiteSpace(TargetDirectoryPath))
            {
                var recomputedPath = Path.Combine(TargetDirectoryPath, value);
                SetProperty(ref targetPath, recomputedPath, nameof(TargetPath));
            }
        }
    }

    /// <summary>
    /// 目标路径。
    /// </summary>
    public string TargetPath
    {
        get => targetPath;
        set
        {
            if (!SetProperty(ref targetPath, value))
            {
                return;
            }

            if (InputMode == LinkTargetInputMode.FullPath)
            {
                suppressDisplayNamePathSync = true;
                DisplayName = Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                suppressDisplayNamePathSync = false;
                TargetDirectoryPath = Path.GetDirectoryName(value);
            }
            else if (!string.Equals(TargetDirectoryPath, Path.GetDirectoryName(value), StringComparison.OrdinalIgnoreCase))
            {
                InputMode = LinkTargetInputMode.FullPath;
                suppressDisplayNamePathSync = true;
                DisplayName = Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                suppressDisplayNamePathSync = false;
                TargetDirectoryPath = Path.GetDirectoryName(value);
            }
        }
    }

    /// <summary>
    /// 编辑期预检查提示。
    /// </summary>
    public string? ValidationMessage
    {
        get => validationMessage;
        set => SetProperty(ref validationMessage, value);
    }

    /// <summary>
    /// 目录优先模式下记录的目标目录。
    /// </summary>
    public string? TargetDirectoryPath
    {
        get => targetDirectoryPath;
        private set => SetProperty(ref targetDirectoryPath, value);
    }

    /// <summary>
    /// 当前目标录入模式。
    /// </summary>
    public LinkTargetInputMode InputMode
    {
        get => inputMode;
        private set => SetProperty(ref inputMode, value);
    }

    /// <summary>
    /// 用“目录 + 名称”的方式配置目标。
    /// </summary>
    public void ApplyDirectoryTarget(string directoryPath, string targetName)
    {
        InputMode = LinkTargetInputMode.DirectoryWithName;
        TargetDirectoryPath = directoryPath;
        suppressDisplayNamePathSync = true;
        displayName = targetName;
        suppressDisplayNamePathSync = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        TargetPath = Path.Combine(directoryPath, targetName);
    }

    /// <summary>
    /// 用完整路径直接配置目标。
    /// </summary>
    public void ApplyFullPath(string fullPath)
    {
        InputMode = LinkTargetInputMode.FullPath;
        TargetDirectoryPath = Path.GetDirectoryName(fullPath);
        TargetPath = fullPath;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<TValue>(ref TValue storage, TValue value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
