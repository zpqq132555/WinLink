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
    private readonly record struct ManagedLinkGroupKey(string DisplayName, string SourcePath, LinkSourceKind SourceKind, ManagedPathMode Mode);

    private readonly ILinkOperationService linkOperationService;
    private readonly ILinkStatusService linkStatusService;
    private readonly PresetTemplateService presetTemplateService;
    private readonly IRegistryStorageService registryStorageService;
    private readonly ILinkTaskWorkbenchService workbenchService;
    private readonly SemaphoreSlim uiStateSaveLock = new(1, 1);
    private readonly Dictionary<ManagedLinkRecord, HashSet<string>> managedLinkSourceRecordIds = new();
    private readonly Dictionary<ManagedLinkTargetRecord, string> managedTargetSourceRecordIds = new();
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
            CreateDraftTask("新任务", string.Empty, LinkSourceKind.Unknown, includeSampleTargets: false),
        ];
        SelectedTask = PendingTasks[0];

        ManagedLinks = [];
        LoadUiStateFromStorage();
        LoadManagedLinksFromStorage();

        Presets =
        [
            new PresetTemplateDefinition
            {
                Id = "preset-agents-main-distribution",
                Name = "AGENTS 主配置分发",
                Description = "把 AGENTS_BAK 主配置分发到 Codex 和 Claude 的主配置文件。",
                SourcePathTemplate = "%USERPROFILE%\\.agents\\skills\\AGENTS_BAK.md",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "Codex AGENTS",
                        TargetPathTemplate = "%USERPROFILE%\\.codex\\AGENTS.md",
                    },
                    new PresetTemplateTarget
                    {
                        DisplayName = "Claude CLAUDE",
                        TargetPathTemplate = "%USERPROFILE%\\.claude\\CLAUDE.md",
                    },
                ],
            },
            new PresetTemplateDefinition
            {
                Id = "preset-skills-directory-sync",
                Name = "skills 目录同步",
                Description = "把 %USERPROFILE%\\.agents\\skills 目录同步到 Claude skills 目录。",
                SourcePathTemplate = "%USERPROFILE%\\.agents\\skills",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "Claude skills",
                        TargetPathTemplate = "%USERPROFILE%\\.claude\\skills",
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
            if (selectedTask is not null)
            {
                selectedTask.PropertyChanged -= SelectedTask_OnPropertyChanged;
            }

            if (!SetProperty(ref selectedTask, value))
            {
                if (selectedTask is not null)
                {
                    selectedTask.PropertyChanged += SelectedTask_OnPropertyChanged;
                }
                return;
            }

            if (value is not null)
            {
                value.PropertyChanged += SelectedTask_OnPropertyChanged;
            }

            SelectedTaskTarget = value?.Targets.FirstOrDefault();
            ResetTargetInputHints();
            if (value is not null)
            {
                workbenchService.RunLightValidation(value);
            }

            OnPropertyChanged(nameof(SelectedTaskReusableManagedSourceRecordId));
            OnPropertyChanged(nameof(IsSelectedTaskReusingManagedSource));
            OnPropertyChanged(nameof(CanEditSelectedTaskSource));
            OnPropertyChanged(nameof(SelectedTaskSourceLockHint));
            OnPropertyChanged(nameof(CanEditSelectedTaskStrategy));
            OnPropertyChanged(nameof(SelectedTaskModeHint));
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
    /// 为当前任务切换或清除“复用已连接源”选择。
    /// </summary>
    public void SelectReusableManagedSourceForSelectedTask(string? recordId)
    {
        if (SelectedTask is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recordId))
        {
            ClearSelectedTaskReusableManagedSource();
            return;
        }

        var option = ReusableManagedSources.FirstOrDefault(candidate =>
            string.Equals(candidate.RecordId, recordId, StringComparison.Ordinal));
        if (option is null)
        {
            return;
        }

        SelectedTask.ReusedManagedSourceRecordId = option.RecordId;
        SelectedTask.DisplayName = option.DisplayName;
        SelectedTask.SourcePath = option.SourcePath;
        SelectedTask.SourceKind = option.SourceKind;
        SelectedTask.Mode = option.Mode;
        SelectedTask.PreferredStrategy = option.PreferredStrategy;
        ResetTargetInputHints();
        workbenchService.RunLightValidation(SelectedTask);
        RefreshSelectedTaskReusableManagedSourceState();
        ShowStatus($"已复用已链接源：{option.DisplayName}");
    }

    /// <summary>
    /// 清除当前任务的已连接源复用状态，并恢复手工编辑模式。
    /// </summary>
    public void ClearSelectedTaskReusableManagedSource()
    {
        if (SelectedTask is null || !SelectedTask.IsReusingManagedSource)
        {
            return;
        }

        SelectedTask.ReusedManagedSourceRecordId = null;
        RefreshSelectedTaskReusableManagedSourceState();
        ShowStatus("已切回手工编辑源定义模式。");
    }

    /// <summary>
    /// 当前任务选中的可复用已连接源记录标识。
    /// </summary>
    public string? SelectedTaskReusableManagedSourceRecordId
    {
        get => SelectedTask?.ReusedManagedSourceRecordId;
        set => SelectReusableManagedSourceForSelectedTask(value);
    }

    /// <summary>
    /// 当前任务是否处于“复用已连接源”模式。
    /// </summary>
    public bool IsSelectedTaskReusingManagedSource => SelectedTask?.IsReusingManagedSource == true;

    /// <summary>
    /// 当前任务是否仍允许手工编辑源定义。
    /// </summary>
    public bool CanEditSelectedTaskSource => !IsSelectedTaskReusingManagedSource;

    /// <summary>
    /// 复用已连接源时展示给用户的提示文案。
    /// </summary>
    public string SelectedTaskSourceLockHint => IsSelectedTaskReusingManagedSource
        ? "当前任务复用了已链接源，源路径和源类型已锁定；你可以继续新增目标。"
        : string.Empty;

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
            OnPropertyChanged(nameof(CanSyncSelectedManagedMirror));
            OnPropertyChanged(nameof(SelectedManagedLinkModeDisplay));
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
    /// 当前选中的受管记录是否支持“同步到所有目标”。
    /// </summary>
    public bool CanSyncSelectedManagedMirror => SelectedManagedLink?.Mode == ManagedPathMode.DirectoryMirror;

    /// <summary>
    /// 当前选中的受管记录模式名称。
    /// </summary>
    public string SelectedManagedLinkModeDisplay => SelectedManagedLink?.Mode switch
    {
        ManagedPathMode.DirectoryMirror => "目录镜像",
        ManagedPathMode.Link => "链接",
        _ => string.Empty,
    };

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
    /// 工作台可选的受管模式列表。
    /// </summary>
    public IReadOnlyList<ManagedPathMode> TaskModeOptions { get; } =
    [
        ManagedPathMode.Link,
        ManagedPathMode.DirectoryMirror,
    ];

    /// <summary>
    /// 当前任务是否仍允许编辑高级链接策略。
    /// </summary>
    public bool CanEditSelectedTaskStrategy => SelectedTask?.Mode != ManagedPathMode.DirectoryMirror;

    /// <summary>
    /// 当前任务模式提示。
    /// </summary>
    public string SelectedTaskModeHint => SelectedTask?.Mode == ManagedPathMode.DirectoryMirror
        ? "镜像模式会真实拷贝目录；后续同步会覆盖目标改动并清理目标中多余内容。"
        : string.Empty;

    /// <summary>
    /// `已链接` 页顶部总览摘要。
    /// </summary>
    public ManagedSummarySnapshot ManagedSummary => new(GetManagedSummary());

    /// <summary>
    /// 新建连接时可直接复用的已连接源候选列表。
    /// </summary>
    public IReadOnlyList<ManagedLinkSourceOption> ReusableManagedSources => BuildReusableManagedSources();

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
    public async Task<LinkExecutionBatchResult> ApplySelectedPresetAsync(LinkExecutionPlan plan, bool allowDowngrade, bool allowMirrorAdoption)
    {
        var result = await linkOperationService.ExecuteAsync(plan, allowDowngrade, allowMirrorAdoption);
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

        SelectedTask.ReusedManagedSourceRecordId = null;
        workbenchService.ApplySource(SelectedTask, sourcePath);
        ResetTargetInputHints();
        RefreshSelectedTaskReusableManagedSourceState();
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
    public async Task<LinkExecutionBatchResult> ExecutePendingTasksAsync(LinkExecutionPlan plan, bool allowDowngrade, bool allowMirrorAdoption)
    {
        var result = await linkOperationService.ExecuteAsync(plan, allowDowngrade, allowMirrorAdoption);
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
        var selectedRecordId = SelectedManagedLink?.Id;
        var selectedTargetId = SelectedManagedTarget?.Id;
        await PersistManagedLinkUiStateAsync();

        var registry = await registryStorageService.LoadRegistryAsync();
        foreach (var record in registry.Records)
        {
            await linkStatusService.RefreshAsync(record);
        }

        await registryStorageService.SaveRegistryAsync(registry);
        ApplyManagedLinks(registry, selectedRecordId, selectedTargetId);
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
        QueueManagedLinkUiStatePersistence();
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

        var recordContext = CreateManagedRecordContext(SelectedManagedLink, target);
        var reason = await linkOperationService.DeleteAsync(recordContext, target);
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

        var recordContext = CreateManagedRecordContext(SelectedManagedLink, target);
        var selectedRecordId = recordContext.Id;
        var selectedTargetId = target.Id;
        var reason = await linkOperationService.RebuildAsync(recordContext, target);
        LoadManagedLinksFromStorage(selectedRecordId, selectedTargetId);
        LoadExecutionHistoryFromStorage();
        ShowStatus(reason is null ? $"已重建目标：{target.DisplayName}" : $"重建链接失败：{target.DisplayName}");
        return reason;
    }

    /// <summary>
    /// 仅移除当前选中目标的受管记录，不影响磁盘上的现有实体。
    /// </summary>
    public async Task<LinkExecutionBatchResult?> SyncSelectedManagedMirrorAsync(bool allowTargetOverwrite)
    {
        if (SelectedManagedLink is null || SelectedManagedLink.Mode != ManagedPathMode.DirectoryMirror)
        {
            return null;
        }

        var sourceRecordIds = managedLinkSourceRecordIds.TryGetValue(SelectedManagedLink, out var ids)
            ? ids.ToList()
            : [SelectedManagedLink.Id];
        var aggregateResult = new LinkExecutionBatchResult
        {
            HistoryEntry = new ExecutionHistoryEntry
            {
                Summary = $"同步目录镜像：{SelectedManagedLink.DisplayName}",
            },
        };
        var selectedTargetId = SelectedManagedTarget?.Id;
        foreach (var sourceRecordId in sourceRecordIds)
        {
            var recordContext = new ManagedLinkRecord
            {
                Id = sourceRecordId,
                DisplayName = SelectedManagedLink.DisplayName,
                SourcePath = SelectedManagedLink.SourcePath,
                SourceKind = SelectedManagedLink.SourceKind,
                Mode = SelectedManagedLink.Mode,
                PreferredStrategy = SelectedManagedLink.PreferredStrategy,
                LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(SelectedManagedLink.LastSynchronizedSourceSnapshot),
                LastSynchronizedAt = SelectedManagedLink.LastSynchronizedAt,
            };
            var result = await linkOperationService.SyncMirrorAsync(recordContext, allowTargetOverwrite);
            aggregateResult.HistoryEntry.SuccessCount += result.HistoryEntry.SuccessCount;
            aggregateResult.HistoryEntry.SkippedCount += result.HistoryEntry.SkippedCount;
            aggregateResult.HistoryEntry.FailedCount += result.HistoryEntry.FailedCount;
            aggregateResult.HistoryEntry.FailureReasons.AddRange(result.HistoryEntry.FailureReasons);
            aggregateResult.ManagedRecords = result.ManagedRecords;
        }
        ExecutionSummary = $"本次执行：成功 {aggregateResult.HistoryEntry.SuccessCount}，跳过 {aggregateResult.HistoryEntry.SkippedCount}，失败 {aggregateResult.HistoryEntry.FailedCount}";
        LoadManagedLinksFromStorage(SelectedManagedLink.Id, selectedTargetId);
        LoadExecutionHistoryFromStorage();
        ShowStatus($"已同步镜像目录：{SelectedManagedLink.DisplayName}");
        return aggregateResult;
    }

    public async Task RemoveManagedTargetRecordAsync(ManagedLinkTargetRecord? target)
    {
        if (SelectedManagedLink is null || target is null)
        {
            return;
        }

        var recordContext = CreateManagedRecordContext(SelectedManagedLink, target);
        var selectedRecordId = recordContext.Id;
        var remainingTargetId = SelectedManagedLink.Targets
            .Where(item => !string.Equals(item.Id, target.Id, StringComparison.Ordinal))
            .Select(item => item.Id)
            .FirstOrDefault();
        await linkOperationService.RemoveRecordAsync(recordContext, target);
        LoadManagedLinksFromStorage(selectedRecordId, remainingTargetId);
        ShowStatus($"已移除记录：{target.DisplayName}");
    }

    /// <summary>
    /// 计算 `已链接` 页顶部摘要。
    /// </summary>
    public IReadOnlyList<ManagedLinkRecord> GetMirrorRecordsNeedingSync()
    {
        return ManagedLinks
            .Where(record => record.Mode == ManagedPathMode.DirectoryMirror &&
                record.Targets.Any(target =>
                    target.State == LinkTargetState.PendingSync ||
                    (target.State == LinkTargetState.Warning &&
                     !string.IsNullOrWhiteSpace(target.StatusReason) &&
                     target.StatusReason.Contains("源目录", StringComparison.Ordinal))))
            .ToList();
    }

    public (int Records, int Targets, int Active, int PendingSync, int Warning, int Invalid, int Disconnected) GetManagedSummary()
    {
        var targets = ManagedLinks.SelectMany(record => record.Targets).ToList();
        return (
            ManagedLinks.Count,
            targets.Count,
            targets.Count(target => target.State == LinkTargetState.Active),
            targets.Count(target => target.State == LinkTargetState.PendingSync),
            targets.Count(target => target.State == LinkTargetState.Warning),
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
            Mode = ManagedPathMode.Link,
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

    private void SelectedTask_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTask))
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(LinkTaskDraft.DisplayName), StringComparison.Ordinal))
        {
            var option = GetSelectedTaskReusableManagedSourceOption();
            if (option is not null && !string.Equals(SelectedTask?.DisplayName, option.DisplayName, StringComparison.Ordinal))
            {
                SelectedTask!.ReusedManagedSourceRecordId = null;
                RefreshSelectedTaskReusableManagedSourceState();
            }

            return;
        }

        if (string.Equals(e.PropertyName, nameof(LinkTaskDraft.ReusedManagedSourceRecordId), StringComparison.Ordinal))
        {
            RefreshSelectedTaskReusableManagedSourceState();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(LinkTaskDraft.Mode), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(LinkTaskDraft.SourceKind), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(CanEditSelectedTaskStrategy));
            OnPropertyChanged(nameof(SelectedTaskModeHint));
        }
    }

    private ManagedLinkSourceOption? GetSelectedTaskReusableManagedSourceOption()
    {
        if (SelectedTask is null || string.IsNullOrWhiteSpace(SelectedTask.ReusedManagedSourceRecordId))
        {
            return null;
        }

        return ReusableManagedSources.FirstOrDefault(option =>
            string.Equals(option.RecordId, SelectedTask.ReusedManagedSourceRecordId, StringComparison.Ordinal));
    }

    private void RefreshSelectedTaskReusableManagedSourceState()
    {
        OnPropertyChanged(nameof(SelectedTaskReusableManagedSourceRecordId));
        OnPropertyChanged(nameof(IsSelectedTaskReusingManagedSource));
        OnPropertyChanged(nameof(CanEditSelectedTaskSource));
        OnPropertyChanged(nameof(SelectedTaskSourceLockHint));
        OnPropertyChanged(nameof(CanEditSelectedTaskStrategy));
        OnPropertyChanged(nameof(SelectedTaskModeHint));
    }

    private void LoadUiStateFromStorage()
    {
        workspaceUiState = registryStorageService.LoadUiStateAsync().GetAwaiter().GetResult();
    }

    private void LoadManagedLinksFromStorage(string? preferredRecordId = null, string? preferredTargetId = null)
    {
        var registry = registryStorageService.LoadRegistryAsync().GetAwaiter().GetResult();
        ApplyManagedLinks(registry, preferredRecordId, preferredTargetId);
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

        QueueManagedLinkUiStatePersistence();
    }

    private void QueueManagedLinkUiStatePersistence()
    {
        UpdateManagedLinkUiStateSnapshot();
        var snapshot = CloneUiState(workspaceUiState);
        _ = PersistManagedLinkUiStateInBackgroundAsync(snapshot);
    }

    private async Task PersistManagedLinkUiStateInBackgroundAsync(WorkspaceUiStateDocument snapshot)
    {
        await uiStateSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await registryStorageService.SaveUiStateAsync(snapshot).ConfigureAwait(false);
        }
        finally
        {
            uiStateSaveLock.Release();
        }
    }

    private async Task PersistManagedLinkUiStateAsync()
    {
        UpdateManagedLinkUiStateSnapshot();
        var snapshot = CloneUiState(workspaceUiState);
        await uiStateSaveLock.WaitAsync();
        try
        {
            await registryStorageService.SaveUiStateAsync(snapshot);
        }
        finally
        {
            uiStateSaveLock.Release();
        }
    }

    private void UpdateManagedLinkUiStateSnapshot()
    {
        workspaceUiState.SelectedManagedLinkId = SelectedManagedLink?.Id;
        workspaceUiState.ExpandedManagedLinkIds = ManagedLinks
            .Where(record => record.IsExpanded)
            .Select(record => record.Id)
            .ToList();
    }

    private static WorkspaceUiStateDocument CloneUiState(WorkspaceUiStateDocument source)
    {
        return new WorkspaceUiStateDocument
        {
            SchemaVersion = source.SchemaVersion,
            SelectedPendingTaskId = source.SelectedPendingTaskId,
            SelectedManagedLinkId = source.SelectedManagedLinkId,
            ExpandedManagedLinkIds = [.. source.ExpandedManagedLinkIds],
        };
    }

    private void ApplyManagedLinks(ManagedLinkRegistryDocument registry, string? preferredRecordId = null, string? preferredTargetId = null)
    {
        ManagedLinks.Clear();
        managedLinkSourceRecordIds.Clear();
        managedTargetSourceRecordIds.Clear();

        var expandedIds = new HashSet<string>(workspaceUiState.ExpandedManagedLinkIds, StringComparer.Ordinal);
        foreach (var record in BuildGroupedManagedLinks(registry.Records, expandedIds))
        {
            ManagedLinks.Add(record);
        }

        suppressManagedLinkUiStatePersistence = true;
        var selectedRecordId = preferredRecordId ?? workspaceUiState.SelectedManagedLinkId;
        SelectedManagedLink = ManagedLinks.FirstOrDefault(record => ContainsManagedRecordId(record, selectedRecordId))
            ?? ManagedLinks.FirstOrDefault();
        if (SelectedManagedLink is not null)
        {
            SelectedManagedTarget = SelectedManagedLink.Targets.FirstOrDefault(target => string.Equals(target.Id, preferredTargetId, StringComparison.Ordinal))
                ?? SelectedManagedLink.Targets.FirstOrDefault();
        }
        suppressManagedLinkUiStatePersistence = false;

        OnPropertyChanged(nameof(ManagedSummary));
        OnPropertyChanged(nameof(ReusableManagedSources));
    }

    private ManagedLinkRecord CreateManagedRecordContext(ManagedLinkRecord groupedRecord, ManagedLinkTargetRecord? target)
    {
        if (target is null || !managedTargetSourceRecordIds.TryGetValue(target, out var recordId))
        {
            recordId = groupedRecord.Id;
        }

        return new ManagedLinkRecord
        {
            Id = recordId,
            DisplayName = groupedRecord.DisplayName,
            SourcePath = groupedRecord.SourcePath,
            SourceKind = groupedRecord.SourceKind,
            Mode = groupedRecord.Mode,
            PreferredStrategy = groupedRecord.PreferredStrategy,
            LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(groupedRecord.LastSynchronizedSourceSnapshot),
            LastSynchronizedAt = groupedRecord.LastSynchronizedAt,
            Targets = target is null ? [] : [CloneManagedTarget(target)],
        };
    }

    private IReadOnlyList<ManagedLinkSourceOption> BuildReusableManagedSources()
    {
        if (ManagedLinks.Count == 0)
        {
            return [];
        }

        var namesRequiringPathDisambiguation = ManagedLinks
            .GroupBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group
                .Select(record => NormalizeManagedLinkPath(record.SourcePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ManagedLinks
            .Select(record => new ManagedLinkSourceOption
            {
                RecordId = record.Id,
                DisplayName = record.DisplayName,
                SourcePath = record.SourcePath,
                SourceKind = record.SourceKind,
                PreferredStrategy = record.PreferredStrategy,
                Mode = record.Mode,
                DisplayLabel = namesRequiringPathDisambiguation.Contains(record.DisplayName)
                    ? $"{record.DisplayName} ({record.SourcePath})"
                    : record.DisplayName,
            })
            .ToList();
    }

    private IEnumerable<ManagedLinkRecord> BuildGroupedManagedLinks(IEnumerable<ManagedLinkRecord> records, HashSet<string> expandedIds)
    {
        return records
            .GroupBy(CreateManagedLinkGroupKey)
            .Select(group =>
            {
                var representative = group.First();
                var sourceRecordIds = group
                    .Select(record => record.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var aggregatedRecord = new ManagedLinkRecord
                {
                    Id = representative.Id,
                    DisplayName = representative.DisplayName,
                    SourcePath = representative.SourcePath,
                    SourceKind = representative.SourceKind,
                    Mode = representative.Mode,
                    PreferredStrategy = representative.PreferredStrategy,
                    LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(representative.LastSynchronizedSourceSnapshot),
                    LastSynchronizedAt = representative.LastSynchronizedAt,
                    CreatedAt = group.Min(record => record.CreatedAt),
                    UpdatedAt = group.Max(record => record.UpdatedAt),
                    IsExpanded = group.Any(record => expandedIds.Contains(record.Id)),
                };

                foreach (var sourceRecord in group)
                {
                    foreach (var sourceTarget in sourceRecord.Targets)
                    {
                        var clonedTarget = CloneManagedTarget(sourceTarget);
                        managedTargetSourceRecordIds[clonedTarget] = sourceRecord.Id;
                        aggregatedRecord.Targets.Add(clonedTarget);
                    }
                }

                managedLinkSourceRecordIds[aggregatedRecord] = sourceRecordIds;
                return aggregatedRecord;
            })
            .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool ContainsManagedRecordId(ManagedLinkRecord record, string? recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return false;
        }

        return managedLinkSourceRecordIds.TryGetValue(record, out var sourceRecordIds)
            ? sourceRecordIds.Contains(recordId)
            : string.Equals(record.Id, recordId, StringComparison.Ordinal);
    }

    private static ManagedLinkGroupKey CreateManagedLinkGroupKey(ManagedLinkRecord record)
    {
        return new ManagedLinkGroupKey(
            record.DisplayName.Trim(),
            NormalizeManagedLinkPath(record.SourcePath),
            record.SourceKind,
            record.Mode);
    }

    private static ManagedLinkTargetRecord CloneManagedTarget(ManagedLinkTargetRecord source)
    {
        return new ManagedLinkTargetRecord
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            TargetPath = source.TargetPath,
            State = source.State,
            StatusReason = source.StatusReason,
            AppliedStrategy = source.AppliedStrategy,
            LastSynchronizedSnapshot = DirectoryMirrorService.CloneSnapshot(source.LastSynchronizedSnapshot),
            LastSynchronizedAt = source.LastSynchronizedAt,
            LastCheckedAt = source.LastCheckedAt,
        };
    }

    private static string NormalizeManagedLinkPath(string sourcePath)
    {
        return sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
    public ManagedSummarySnapshot((int Records, int Targets, int Active, int PendingSync, int Warning, int Invalid, int Disconnected) values)
    {
        Records = values.Records;
        Targets = values.Targets;
        Active = values.Active;
        PendingSync = values.PendingSync;
        Warning = values.Warning;
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

    public int PendingSync { get; }

    public int Warning { get; }

    /// <summary>
    /// 失效目标数量。
    /// </summary>
    public int Invalid { get; }

    /// <summary>
    /// 已断开目标数量。
    /// </summary>
    public int Disconnected { get; }
}
