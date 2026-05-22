using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using WinLink.App.Infrastructure;
using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.ViewModels;

/// <summary>
/// 为主窗口提供三大工作区的数据、工作台编辑能力，以及受管链接的刷新与维护动作。
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ILinkOperationService linkOperationService;
    private readonly ILinkStatusService linkStatusService;
    private readonly PresetTemplateService presetTemplateService;
    private readonly IRegistryStorageService registryStorageService;
    private readonly ILinkTaskWorkbenchService workbenchService;
    private bool suppressManagedLinkUiStatePersistence;
    private string directoryTargetDirectoryInput = string.Empty;
    private string directoryTargetNameInput = string.Empty;
    private string executionSummary = "尚未执行批量创建。";
    private string fullTargetPathInput = string.Empty;
    private ManagedLinkRecord? selectedManagedLink;
    private ManagedLinkTargetRecord? selectedManagedTarget;
    private PresetTemplateDefinition? selectedPreset;
    private PresetTemplatePreview? selectedPresetPreview;
    private string statusMessage = string.Empty;
    private LinkTaskDraft? selectedTask;
    private LinkTargetDraft? selectedTaskTarget;
    private string validationSummary = "尚未运行完整校验。";
    private WorkspaceUiStateDocument workspaceUiState = new();

    /// <summary>
    /// 使用基础目录、路径服务和各类业务服务构造主窗口状态。
    /// </summary>
    public ShellViewModel(
        AppDirectories directories,
        IPathEnvironmentService pathEnvironmentService,
        ILinkTaskWorkbenchService workbenchService,
        ILinkOperationService linkOperationService,
        ILinkStatusService linkStatusService,
        PresetTemplateService presetTemplateService,
        IRegistryStorageService registryStorageService)
    {
        this.workbenchService = workbenchService;
        this.linkOperationService = linkOperationService;
        this.linkStatusService = linkStatusService;
        this.presetTemplateService = presetTemplateService;
        this.registryStorageService = registryStorageService;

        StorageRoot = directories.RootDirectory;
        StatusMessage = $"就绪，数据目录：{directories.RootDirectory}";

        PendingTasks =
        [
            CreateDraftTask("AGENTS 分发", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AGENTS.md"), LinkSourceKind.File, includeSampleTargets: true),
        ];
        SelectedTask = PendingTasks[0];

        ManagedLinks = [];
        LoadUiStateFromStorage();
        LoadManagedLinksFromStorage();

        Presets =
        [
            new PresetTemplateDefinition
            {
                Name = "AGENTS 主配置分发",
                Description = "把当前用户主目录下的 AGENTS 配置映射到常用工作目录。",
                SourcePathTemplate = "%USERPROFILE%\\AGENTS.md",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "当前工作区",
                        TargetPathTemplate = "%WORKSPACE%\\AGENTS.md",
                    },
                ],
            },
            new PresetTemplateDefinition
            {
                Name = "skills 目录同步",
                Description = "把用户级 skills 目录映射到当前工作区，便于本地扩展共享。",
                SourcePathTemplate = "%USERPROFILE%\\.codex\\skills",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "工作区 skills",
                        TargetPathTemplate = "%WORKSPACE%\\.codex\\skills",
                    },
                ],
            },
        ];
        SelectedPreset = Presets[0];

        ExecutionHistory = [];
        LoadExecutionHistoryFromStorage();
    }

    /// <summary>
    /// 数据目录位置。
    /// </summary>
    public string StorageRoot { get; }

    /// <summary>
    /// 当前会话中的待执行任务。
    /// </summary>
    public ObservableCollection<LinkTaskDraft> PendingTasks { get; }

    /// <summary>
    /// 当前选中的待执行任务。
    /// </summary>
    public LinkTaskDraft? SelectedTask
    {
        get => selectedTask;
        set
        {
            if (!SetProperty(ref selectedTask, value))
            {
                return;
            }

            SelectedTaskTarget = value?.Targets.FirstOrDefault();
            ResetTargetInputHints();
            if (value is not null)
            {
                workbenchService.RunLightValidation(value);
            }
        }
    }

    /// <summary>
    /// 当前选中的目标草稿。
    /// </summary>
    public LinkTargetDraft? SelectedTaskTarget
    {
        get => selectedTaskTarget;
        set => SetProperty(ref selectedTaskTarget, value);
    }

    /// <summary>
    /// 已记录的受管源任务列表。
    /// </summary>
    public ObservableCollection<ManagedLinkRecord> ManagedLinks { get; }

    /// <summary>
    /// 当前选中的受管源任务。
    /// </summary>
    public ManagedLinkRecord? SelectedManagedLink
    {
        get => selectedManagedLink;
        set
        {
            if (!SetProperty(ref selectedManagedLink, value))
            {
                return;
            }

            SelectedManagedTarget = value?.Targets.FirstOrDefault();
            PersistManagedLinkUiStateIfNeeded();
        }
    }

    /// <summary>
    /// 当前选中的受管目标项。
    /// </summary>
    public ManagedLinkTargetRecord? SelectedManagedTarget
    {
        get => selectedManagedTarget;
        set => SetProperty(ref selectedManagedTarget, value);
    }

    /// <summary>
    /// 内置预设模板列表。
    /// </summary>
    public ObservableCollection<PresetTemplateDefinition> Presets { get; }

    /// <summary>
    /// 当前选中的预设模板。
    /// </summary>
    public PresetTemplateDefinition? SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (!SetProperty(ref selectedPreset, value))
            {
                return;
            }

            SelectedPresetPreview = value is null ? null : presetTemplateService.BuildPreview(value);
        }
    }

    /// <summary>
    /// 当前选中预设在本机上的真实预览。
    /// </summary>
    public PresetTemplatePreview? SelectedPresetPreview
    {
        get => selectedPresetPreview;
        private set => SetProperty(ref selectedPresetPreview, value);
    }

    /// <summary>
    /// 最近执行历史摘要。
    /// </summary>
    public ObservableCollection<ExecutionHistoryEntry> ExecutionHistory { get; }

    /// <summary>
    /// 工作台可选的高级策略列表。
    /// </summary>
    public IReadOnlyList<LinkCreationStrategy> LinkStrategyOptions { get; } =
    [
        LinkCreationStrategy.Auto,
        LinkCreationStrategy.SymbolicLink,
        LinkCreationStrategy.Junction,
        LinkCreationStrategy.HardLink,
    ];

    /// <summary>
    /// `已链接` 页顶部总览摘要。
    /// </summary>
    public ManagedSummarySnapshot ManagedSummary => new(GetManagedSummary());

    /// <summary>
    /// 目录优先模式下的目标目录输入。
    /// </summary>
    public string DirectoryTargetDirectoryInput
    {
        get => directoryTargetDirectoryInput;
        set => SetProperty(ref directoryTargetDirectoryInput, value);
    }

    /// <summary>
    /// 目录优先模式下的目标名称输入。
    /// </summary>
    public string DirectoryTargetNameInput
    {
        get => directoryTargetNameInput;
        set => SetProperty(ref directoryTargetNameInput, value);
    }

    /// <summary>
    /// 完整路径模式下的目标路径输入。
    /// </summary>
    public string FullTargetPathInput
    {
        get => fullTargetPathInput;
        set => SetProperty(ref fullTargetPathInput, value);
    }

    /// <summary>
    /// 最近一次完整校验的摘要。
    /// </summary>
    public string ValidationSummary
    {
        get => validationSummary;
        set => SetProperty(ref validationSummary, value);
    }

    /// <summary>
    /// 最近一次执行的结果摘要。
    /// </summary>
    public string ExecutionSummary
    {
        get => executionSummary;
        set => SetProperty(ref executionSummary, value);
    }

    /// <summary>
    /// 状态栏消息。
    /// </summary>
    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    /// <summary>
    /// 更新主窗口底部状态消息。
    /// </summary>
    public void ShowStatus(string message)
    {
        StatusMessage = message;
    }

    /// <summary>
    /// 新建一个空任务并切换到该任务。
    /// </summary>
    public void AddPendingTask()
    {
        var task = CreateDraftTask("新任务", string.Empty, LinkSourceKind.Unknown, includeSampleTargets: false);
        PendingTasks.Add(task);
        SelectedTask = task;
        ShowStatus("已添加一条新的待执行任务。");
    }

    /// <summary>
    /// 移除当前选中的任务。
    /// </summary>
    public void RemoveSelectedTask()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var index = PendingTasks.IndexOf(SelectedTask);
        PendingTasks.Remove(SelectedTask);
        SelectedTask = PendingTasks.Count == 0
            ? null
            : PendingTasks[Math.Clamp(index, 0, PendingTasks.Count - 1)];
        ShowStatus("已移除当前任务。");
    }

    /// <summary>
    /// 把当前预设转成普通待执行任务，并切回工作台继续编辑。
    /// </summary>
    public void ConvertSelectedPresetToPendingTask()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        var task = presetTemplateService.InstantiateTask(SelectedPreset);
        workbenchService.RunLightValidation(task);
        PendingTasks.Add(task);
        SelectedTask = task;
        ShowStatus($"已将预设“{SelectedPreset.Name}”转成普通任务。");
    }

    /// <summary>
    /// 对当前选中的预设执行与普通任务一致的完整校验。
    /// </summary>
    public Task<IReadOnlyList<string>> ValidateSelectedPresetAsync()
    {
        var task = CreateSelectedPresetTask();
        return task is null
            ? Task.FromResult<IReadOnlyList<string>>(["请先选择一个预设。"])
            : linkOperationService.ValidateAsync([task]);
    }

    /// <summary>
    /// 为当前选中的预设生成执行计划，以便沿用同一套降级确认流程。
    /// </summary>
    public Task<LinkExecutionPlan> BuildSelectedPresetExecutionPlanAsync()
    {
        var task = CreateSelectedPresetTask();
        return task is null
            ? Task.FromResult(new LinkExecutionPlan())
            : linkOperationService.PlanExecutionAsync([task]);
    }

    /// <summary>
    /// 直接应用当前预设，并复用标准执行、注册表和历史记录管道。
    /// </summary>
    public async Task<LinkExecutionBatchResult> ApplySelectedPresetAsync(LinkExecutionPlan plan, bool allowDowngrade)
    {
        var result = await linkOperationService.ExecuteAsync(plan, allowDowngrade);
        ExecutionSummary = $"本次执行：成功 {result.HistoryEntry.SuccessCount}，跳过 {result.HistoryEntry.SkippedCount}，失败 {result.HistoryEntry.FailedCount}";
        LoadManagedLinksFromStorage();
        LoadExecutionHistoryFromStorage();
        ShowStatus($"已直接应用预设：{SelectedPreset?.Name}");
        return result;
    }

    /// <summary>
    /// 根据给定源路径更新当前任务，并自动锁定源类型。
    /// </summary>
    public void ApplySourceToSelectedTask(string sourcePath)
    {
        if (SelectedTask is null)
        {
            return;
        }

        workbenchService.ApplySource(SelectedTask, sourcePath);
        ResetTargetInputHints();
        ShowStatus($"已更新源路径并锁定类型：{SelectedTask.SourceKind}");
    }

    /// <summary>
    /// 为当前任务添加一个按目录生成默认名称的目标。
    /// </summary>
    public void AddDirectoryTargetToSelectedTask()
    {
        if (SelectedTask is null || string.IsNullOrWhiteSpace(DirectoryTargetDirectoryInput))
        {
            return;
        }

        var target = workbenchService.AddTargetFromDirectory(SelectedTask, DirectoryTargetDirectoryInput, DirectoryTargetNameInput);
        SelectedTaskTarget = target;
        DirectoryTargetDirectoryInput = string.Empty;
        DirectoryTargetNameInput = GetSuggestedTargetName();
        ShowStatus($"已添加目标：{target.TargetPath}");
    }

    /// <summary>
    /// 为当前任务添加一个完整路径目标。
    /// </summary>
    public void AddFullPathTargetToSelectedTask()
    {
        if (SelectedTask is null || string.IsNullOrWhiteSpace(FullTargetPathInput))
        {
            return;
        }

        var target = workbenchService.AddTargetFromFullPath(SelectedTask, FullTargetPathInput);
        SelectedTaskTarget = target;
        FullTargetPathInput = string.Empty;
        ShowStatus($"已添加完整路径目标：{target.TargetPath}");
    }

    /// <summary>
    /// 删除当前选中的目标项。
    /// </summary>
    public void RemoveSelectedTarget()
    {
        if (SelectedTask is null || SelectedTaskTarget is null)
        {
            return;
        }

        workbenchService.RemoveTarget(SelectedTask, SelectedTaskTarget);
        SelectedTaskTarget = SelectedTask.Targets.FirstOrDefault();
        ShowStatus("已移除选中的目标项。");
    }

    /// <summary>
    /// 重新运行当前任务的编辑期轻量预检查。
    /// </summary>
    public void RefreshSelectedTaskLightValidation()
    {
        if (SelectedTask is null)
        {
            return;
        }

        workbenchService.RunLightValidation(SelectedTask);
        ShowStatus("已刷新当前任务的轻量预检查结果。");
    }

    /// <summary>
    /// 对全部待执行任务运行完整校验，并返回问题列表。
    /// </summary>
    public async Task<IReadOnlyList<string>> ValidateAllPendingTasksAsync()
    {
        var issues = await linkOperationService.ValidateAsync(PendingTasks);
        ValidationSummary = issues.Count == 0
            ? $"校验通过，共检查 {PendingTasks.Count} 条任务。"
            : $"校验发现 {issues.Count} 个问题，请先处理后再执行。";
        ShowStatus(ValidationSummary);
        return issues;
    }

    /// <summary>
    /// 生成当前待执行任务的执行计划，用于决定是否需要统一降级确认。
    /// </summary>
    public Task<LinkExecutionPlan> BuildExecutionPlanAsync()
    {
        return linkOperationService.PlanExecutionAsync(PendingTasks);
    }

    /// <summary>
    /// 执行当前批次任务，并在完成后刷新注册表与历史视图。
    /// </summary>
    public async Task<LinkExecutionBatchResult> ExecutePendingTasksAsync(LinkExecutionPlan plan, bool allowDowngrade)
    {
        var result = await linkOperationService.ExecuteAsync(plan, allowDowngrade);
        ExecutionSummary = $"本次执行：成功 {result.HistoryEntry.SuccessCount}，跳过 {result.HistoryEntry.SkippedCount}，失败 {result.HistoryEntry.FailedCount}";
        ShowStatus(ExecutionSummary);

        LoadManagedLinksFromStorage();
        LoadExecutionHistoryFromStorage();
        return result;
    }

    /// <summary>
    /// 进入 `已链接` 页或用户手动请求时，刷新全部受管目标的状态并回写注册表。
    /// </summary>
    public async Task RefreshManagedLinksAsync(bool autoTriggered = false)
    {
        PersistManagedLinkUiState();

        var registry = registryStorageService.LoadRegistryAsync().GetAwaiter().GetResult();
        foreach (var record in registry.Records)
        {
            await linkStatusService.RefreshAsync(record);
        }

        await registryStorageService.SaveRegistryAsync(registry);
        LoadManagedLinksFromStorage();
        ShowStatus(autoTriggered ? "已自动刷新受管链接状态。" : "已刷新受管链接状态。");
    }

    /// <summary>
    /// 记录当前选中受管源任务的展开状态，并立即持久化到轻量 UI 状态文件。
    /// </summary>
    public void SetManagedLinkExpanded(ManagedLinkRecord? record, bool isExpanded)
    {
        if (record is null)
        {
            return;
        }

        record.IsExpanded = isExpanded;
        PersistManagedLinkUiState();
    }

    /// <summary>
    /// 对当前选中的受管目标执行严格删除；成功后仅保留可重建的记录。
    /// </summary>
    public async Task<string?> DeleteManagedTargetAsync(ManagedLinkTargetRecord? target)
    {
        if (SelectedManagedLink is null || target is null)
        {
            return "请先选择一个受管目标。";
        }

        var reason = await linkOperationService.DeleteAsync(SelectedManagedLink, target);
        if (reason is null)
        {
            await RefreshManagedLinksAsync();
            ShowStatus($"已删除链接：{target.DisplayName}");
        }
        else
        {
            ShowStatus($"删除链接已被阻止：{target.DisplayName}");
        }

        return reason;
    }

    /// <summary>
    /// 对当前选中的失效或已断开目标执行重建，并复用标准执行与历史记录管道。
    /// </summary>
    public async Task<string?> RebuildManagedTargetAsync(ManagedLinkTargetRecord? target)
    {
        if (SelectedManagedLink is null || target is null)
        {
            return "请先选择一个受管目标。";
        }

        var reason = await linkOperationService.RebuildAsync(SelectedManagedLink, target);
        LoadManagedLinksFromStorage();
        LoadExecutionHistoryFromStorage();
        ShowStatus(reason is null ? $"已重建目标：{target.DisplayName}" : $"重建链接失败：{target.DisplayName}");
        return reason;
    }

    /// <summary>
    /// 仅移除当前选中目标的受管记录，不影响磁盘上的现有实体。
    /// </summary>
    public async Task RemoveManagedTargetRecordAsync(ManagedLinkTargetRecord? target)
    {
        if (SelectedManagedLink is null || target is null)
        {
            return;
        }

        await linkOperationService.RemoveRecordAsync(SelectedManagedLink, target);
        LoadManagedLinksFromStorage();
        ShowStatus($"已移除记录：{target.DisplayName}");
    }

    /// <summary>
    /// 计算 `已链接` 页顶部摘要。
    /// </summary>
    public (int Records, int Targets, int Active, int Invalid, int Disconnected) GetManagedSummary()
    {
        var targets = ManagedLinks.SelectMany(record => record.Targets).ToList();
        return (
            ManagedLinks.Count,
            targets.Count,
            targets.Count(target => target.State == LinkTargetState.Active),
            targets.Count(target => target.State == LinkTargetState.Invalid),
            targets.Count(target => target.State == LinkTargetState.Disconnected));
    }

    /// <summary>
    /// 展开预设中的工作区变量，便于在 UI 中预览真实路径。
    /// </summary>
    public string ExpandPresetPath(string path)
    {
        return path.Replace("%WORKSPACE%", Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private static LinkTaskDraft CreateDraftTask(string displayName, string sourcePath, LinkSourceKind sourceKind, bool includeSampleTargets)
    {
        var task = new LinkTaskDraft
        {
            DisplayName = displayName,
            SourcePath = sourcePath,
            SourceKind = sourceKind,
        };

        if (includeSampleTargets)
        {
            task.Targets.Add(new LinkTargetDraft
            {
                DisplayName = "Desktop",
                TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AGENTS.md"),
            });
            task.Targets.Add(new LinkTargetDraft
            {
                DisplayName = "Workspace",
                TargetPath = Path.Combine(Environment.CurrentDirectory, "AGENTS.md"),
            });
        }

        return task;
    }

    private string GetSuggestedTargetName()
    {
        if (SelectedTask is null || string.IsNullOrWhiteSpace(SelectedTask.SourcePath))
        {
            return string.Empty;
        }

        var normalizedPath = SelectedTask.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalizedPath);
    }

    private void ResetTargetInputHints()
    {
        DirectoryTargetNameInput = GetSuggestedTargetName();
        FullTargetPathInput = string.Empty;
        DirectoryTargetDirectoryInput = string.Empty;
    }

    private void LoadUiStateFromStorage()
    {
        workspaceUiState = registryStorageService.LoadUiStateAsync().GetAwaiter().GetResult();
    }

    private void LoadManagedLinksFromStorage()
    {
        ManagedLinks.Clear();

        var expandedIds = new HashSet<string>(workspaceUiState.ExpandedManagedLinkIds, StringComparer.Ordinal);
        var registry = registryStorageService.LoadRegistryAsync().GetAwaiter().GetResult();
        foreach (var record in registry.Records)
        {
            record.IsExpanded = expandedIds.Contains(record.Id);
            ManagedLinks.Add(record);
        }

        suppressManagedLinkUiStatePersistence = true;
        SelectedManagedLink = ManagedLinks.FirstOrDefault(record => string.Equals(record.Id, workspaceUiState.SelectedManagedLinkId, StringComparison.Ordinal))
            ?? ManagedLinks.FirstOrDefault();
        suppressManagedLinkUiStatePersistence = false;

        OnPropertyChanged(nameof(ManagedSummary));
    }

    private void LoadExecutionHistoryFromStorage()
    {
        ExecutionHistory.Clear();
        var history = registryStorageService.LoadHistoryAsync().GetAwaiter().GetResult();
        foreach (var entry in history.Entries)
        {
            ExecutionHistory.Add(entry);
        }
    }

    private void PersistManagedLinkUiStateIfNeeded()
    {
        if (suppressManagedLinkUiStatePersistence)
        {
            return;
        }

        PersistManagedLinkUiState();
    }

    private void PersistManagedLinkUiState()
    {
        workspaceUiState.SelectedManagedLinkId = SelectedManagedLink?.Id;
        workspaceUiState.ExpandedManagedLinkIds = ManagedLinks
            .Where(record => record.IsExpanded)
            .Select(record => record.Id)
            .ToList();
        registryStorageService.SaveUiStateAsync(workspaceUiState).GetAwaiter().GetResult();
    }

    private bool SetProperty<TValue>(ref TValue storage, TValue value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private LinkTaskDraft? CreateSelectedPresetTask()
    {
        return SelectedPreset is null ? null : presetTemplateService.InstantiateTask(SelectedPreset);
    }
}

/// <summary>
/// 表示 `已链接` 页面顶部展示的聚合摘要。
/// </summary>
public sealed class ManagedSummarySnapshot
{
    /// <summary>
    /// 用汇总元组创建只读摘要对象。
    /// </summary>
    public ManagedSummarySnapshot((int Records, int Targets, int Active, int Invalid, int Disconnected) values)
    {
        Records = values.Records;
        Targets = values.Targets;
        Active = values.Active;
        Invalid = values.Invalid;
        Disconnected = values.Disconnected;
    }

    /// <summary>
    /// 受管源任务数量。
    /// </summary>
    public int Records { get; }

    /// <summary>
    /// 目标总数。
    /// </summary>
    public int Targets { get; }

    /// <summary>
    /// 生效中的目标数量。
    /// </summary>
    public int Active { get; }

    /// <summary>
    /// 失效目标数量。
    /// </summary>
    public int Invalid { get; }

    /// <summary>
    /// 已断开目标数量。
    /// </summary>
    public int Disconnected { get; }
}
