using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 负责完整校验、执行规划、尽力执行，以及受管记录的严格删除、重建与移除。
/// </summary>
public sealed class LinkOperationService : ILinkOperationService
{
    private readonly ILinkBackendService backendService;
    private readonly IPathEnvironmentService pathEnvironmentService;
    private readonly IRegistryStorageService registryStorageService;
    private readonly ILinkTaskWorkbenchService workbenchService;

    /// <summary>
    /// 使用路径服务、工作台服务、持久化服务和真实后端创建链接执行服务。
    /// </summary>
    public LinkOperationService(
        IPathEnvironmentService pathEnvironmentService,
        ILinkTaskWorkbenchService workbenchService,
        IRegistryStorageService registryStorageService,
        ILinkBackendService backendService)
    {
        this.pathEnvironmentService = pathEnvironmentService;
        this.workbenchService = workbenchService;
        this.registryStorageService = registryStorageService;
        this.backendService = backendService;
    }

    /// <summary>
    /// 对批量任务生成执行计划，并汇总所有需要统一确认的降级项。
    /// </summary>
    public Task<LinkExecutionPlan> PlanExecutionAsync(IEnumerable<LinkTaskDraft> tasks)
    {
        var plan = new LinkExecutionPlan();

        foreach (var task in tasks)
        {
            foreach (var target in task.Targets)
            {
                var plannedTarget = BuildPlannedTarget(task, target);
                plan.Targets.Add(plannedTarget);

                if (plannedTarget.RequiresDowngrade)
                {
                    plan.Downgrades.Add(new LinkExecutionDowngradeItem
                    {
                        TaskDisplayName = task.DisplayName,
                        TargetDisplayName = target.DisplayName,
                        TargetPath = target.TargetPath,
                        RecommendedStrategy = plannedTarget.RecommendedStrategy,
                        FallbackStrategy = plannedTarget.PlannedStrategy,
                    });
                }
            }
        }

        return Task.FromResult(plan);
    }

    /// <summary>
    /// 对批量任务执行完整校验，覆盖源存在性、目标冲突和策略可用性。
    /// </summary>
    public async Task<IReadOnlyList<string>> ValidateAsync(IEnumerable<LinkTaskDraft> tasks)
    {
        var issues = new List<string>();
        var plan = await PlanExecutionAsync(tasks);

        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.DisplayName))
            {
                issues.Add("存在未命名任务，请补充备注名。");
            }

            if (string.IsNullOrWhiteSpace(task.SourcePath))
            {
                issues.Add($"任务“{task.DisplayName}”缺少源路径。");
                continue;
            }

            var expandedSource = pathEnvironmentService.Expand(task.SourcePath);
            var actualKind = pathEnvironmentService.DetectSourceKind(expandedSource);
            if (actualKind == LinkSourceKind.Unknown)
            {
                issues.Add($"任务“{task.DisplayName}”的源不存在：{expandedSource}");
            }
            else if (task.SourceKind != LinkSourceKind.Unknown && task.SourceKind != actualKind)
            {
                issues.Add($"任务“{task.DisplayName}”的源类型已变化，请重新锁定源类型。");
            }

            if (task.Targets.Count == 0)
            {
                issues.Add($"任务“{task.DisplayName}”还没有目标项。");
                continue;
            }

            workbenchService.RunLightValidation(task);

            foreach (var target in task.Targets)
            {
                if (!string.IsNullOrWhiteSpace(target.ValidationMessage))
                {
                    issues.Add($"任务“{task.DisplayName}”的目标“{target.DisplayName}”：{target.ValidationMessage}");
                }

                if (HasInvalidPathChars(target.TargetPath))
                {
                    issues.Add($"任务“{task.DisplayName}”的目标路径格式无效：{target.TargetPath}");
                    continue;
                }

                var parentDirectory = Path.GetDirectoryName(target.TargetPath);
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    issues.Add($"任务“{task.DisplayName}”的目标缺少父目录：{target.TargetPath}");
                    continue;
                }

                if (!Path.IsPathRooted(target.TargetPath))
                {
                    issues.Add($"任务“{task.DisplayName}”的目标必须是完整路径：{target.TargetPath}");
                    continue;
                }

                var rootPath = Path.GetPathRoot(target.TargetPath);
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                {
                    issues.Add($"任务“{task.DisplayName}”的目标磁盘根路径不可用：{target.TargetPath}");
                }
            }
        }

        foreach (var plannedTarget in plan.Targets.Where(item => item.PlannedStrategy == LinkCreationStrategy.Auto))
        {
            issues.Add($"任务“{plannedTarget.TaskDisplayName}”的目标“{plannedTarget.Target.DisplayName}”缺少可用的链接策略。");
        }

        return issues;
    }

    /// <summary>
    /// 按执行计划执行批量创建，并以尽力执行方式记录成功、跳过和失败。
    /// </summary>
    public async Task<LinkExecutionBatchResult> ExecuteAsync(LinkExecutionPlan plan, bool allowDowngrade)
    {
        if (plan.Downgrades.Count > 0 && !allowDowngrade)
        {
            throw new InvalidOperationException("存在未确认的降级项，不能直接执行。");
        }

        var registry = await registryStorageService.LoadRegistryAsync();
        var history = await registryStorageService.LoadHistoryAsync();

        var managedRecordsByTask = new Dictionary<string, ManagedLinkRecord>(StringComparer.Ordinal);
        var historyEntry = new ExecutionHistoryEntry
        {
            Summary = string.Join("、", plan.Targets.Select(target => target.TaskDisplayName).Distinct()),
        };

        foreach (var plannedTarget in plan.Targets)
        {
            if (File.Exists(plannedTarget.Target.TargetPath) || Directory.Exists(plannedTarget.Target.TargetPath))
            {
                historyEntry.SkippedCount++;
                historyEntry.FailureReasons.Add($"目标已存在，已跳过：{plannedTarget.Target.TargetPath}");
                continue;
            }

            try
            {
                await backendService.CreateAsync(
                    plannedTarget.SourceKind,
                    plannedTarget.SourcePath,
                    plannedTarget.Target.TargetPath,
                    plannedTarget.PlannedStrategy);

                historyEntry.SuccessCount++;

                if (!managedRecordsByTask.TryGetValue(plannedTarget.TaskId, out var record))
                {
                    record = new ManagedLinkRecord
                    {
                        Id = plannedTarget.TaskId,
                        DisplayName = plannedTarget.TaskDisplayName,
                        SourcePath = plannedTarget.SourcePath,
                        SourceKind = plannedTarget.SourceKind,
                        PreferredStrategy = plannedTarget.RecommendedStrategy,
                    };
                    managedRecordsByTask.Add(plannedTarget.TaskId, record);
                }

                record.Targets.Add(new ManagedLinkTargetRecord
                {
                    Id = plannedTarget.Target.Id,
                    DisplayName = plannedTarget.Target.DisplayName,
                    TargetPath = plannedTarget.Target.TargetPath,
                    AppliedStrategy = plannedTarget.PlannedStrategy,
                    State = LinkTargetState.Active,
                    StatusReason = "最近一次执行创建成功。",
                    LastCheckedAt = DateTimeOffset.Now,
                });
                record.UpdatedAt = DateTimeOffset.Now;
            }
            catch (Exception ex)
            {
                historyEntry.FailedCount++;
                historyEntry.FailureReasons.Add($"{plannedTarget.Target.TargetPath}：{ex.Message}");
            }
        }

        MergeManagedRecords(registry, managedRecordsByTask.Values);
        history.Entries.Insert(0, historyEntry);
        history.Entries = history.Entries.Take(20).ToList();

        await registryStorageService.SaveRegistryAsync(registry);
        await registryStorageService.SaveHistoryAsync(history);

        return new LinkExecutionBatchResult
        {
            HistoryEntry = historyEntry,
            ManagedRecords = registry.Records,
        };
    }

    /// <summary>
    /// 只在当前目标仍与记录严格匹配时删除链接对象；否则返回明确的阻止原因。
    /// </summary>
    public async Task<string?> DeleteAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
    {
        var inspection = ManagedLinkTargetInspector.Inspect(record.SourceKind, record.SourcePath, target);
        if (!inspection.CanDeleteSafely)
        {
            return inspection.EntryExists
                ? inspection.Reason
                : "目标已不存在，无需再删除链接。你可以改用“移除记录”清理登记。";
        }

        DeleteTargetEntity(record.SourceKind, target);
        await UpdateStoredTargetAsync(record, target, storedTarget =>
        {
            storedTarget.State = LinkTargetState.Disconnected;
            storedTarget.StatusReason = "链接对象已删除，可按需重建。";
            storedTarget.LastCheckedAt = DateTimeOffset.Now;
        });
        return null;
    }

    /// <summary>
    /// 复用标准创建管道重新创建某个已记录目标；若当前对象仍占位，会先做安全删除检查。
    /// </summary>
    public async Task<string?> RebuildAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
    {
        var registry = await registryStorageService.LoadRegistryAsync();
        var currentRecord = FindRecord(registry, record.Id) ?? record;
        var currentTarget = FindTarget(currentRecord, target.Id, target.TargetPath) ?? target;

        var inspection = ManagedLinkTargetInspector.Inspect(currentRecord.SourceKind, currentRecord.SourcePath, currentTarget);
        if (inspection.EntryExists)
        {
            if (!inspection.CanDeleteSafely)
            {
                return $"当前目标无法安全重建：{inspection.Reason}";
            }

            DeleteTargetEntity(currentRecord.SourceKind, currentTarget);
        }

        if (pathEnvironmentService.DetectSourceKind(currentRecord.SourcePath) == LinkSourceKind.Unknown)
        {
            return $"源路径不存在，无法重建：{currentRecord.SourcePath}";
        }

        var task = new LinkTaskDraft
        {
            Id = currentRecord.Id,
            DisplayName = currentRecord.DisplayName,
            SourcePath = currentRecord.SourcePath,
            SourceKind = currentRecord.SourceKind,
            PreferredStrategy = currentTarget.AppliedStrategy,
        };
        var draftTarget = new LinkTargetDraft
        {
            Id = currentTarget.Id,
        };
        draftTarget.ApplyFullPath(currentTarget.TargetPath);
        draftTarget.DisplayName = currentTarget.DisplayName;
        task.Targets.Add(draftTarget);

        var plan = await PlanExecutionAsync([task]);
        var plannedTarget = plan.Targets.Single();
        if (plannedTarget.PlannedStrategy == LinkCreationStrategy.Auto)
        {
            return $"当前环境无法为目标“{currentTarget.DisplayName}”选择可用策略，无法重建。";
        }

        var result = await ExecuteAsync(plan, allowDowngrade: true);
        if (result.HistoryEntry.SuccessCount == 0)
        {
            return result.HistoryEntry.FailureReasons.FirstOrDefault() ?? $"目标“{currentTarget.DisplayName}”重建失败。";
        }

        return null;
    }

    /// <summary>
    /// 仅从注册表中移除某个目标记录，不影响当前磁盘实体。
    /// </summary>
    public async Task RemoveRecordAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
    {
        var registry = await registryStorageService.LoadRegistryAsync();
        var storedRecord = FindRecord(registry, record.Id);
        if (storedRecord is null)
        {
            return;
        }

        storedRecord.Targets.RemoveAll(item =>
            string.Equals(item.Id, target.Id, StringComparison.Ordinal) ||
            string.Equals(item.TargetPath, target.TargetPath, StringComparison.OrdinalIgnoreCase));

        if (storedRecord.Targets.Count == 0)
        {
            registry.Records.RemoveAll(item => string.Equals(item.Id, storedRecord.Id, StringComparison.Ordinal));
        }
        else
        {
            storedRecord.UpdatedAt = DateTimeOffset.Now;
        }

        await registryStorageService.SaveRegistryAsync(registry);
    }

    private PlannedLinkTarget BuildPlannedTarget(LinkTaskDraft task, LinkTargetDraft target)
    {
        var recommendedStrategy = GetRecommendedStrategy(task.SourceKind);
        var plannedStrategy = ResolvePlannedStrategy(task.SourceKind, task.PreferredStrategy, recommendedStrategy, out var requiresDowngrade);

        return new PlannedLinkTarget
        {
            TaskId = task.Id,
            TaskDisplayName = task.DisplayName,
            SourcePath = task.SourcePath,
            SourceKind = task.SourceKind,
            Target = target,
            RecommendedStrategy = recommendedStrategy,
            PlannedStrategy = plannedStrategy,
            RequiresDowngrade = requiresDowngrade,
        };
    }

    private LinkCreationStrategy ResolvePlannedStrategy(
        LinkSourceKind sourceKind,
        LinkCreationStrategy manualStrategy,
        LinkCreationStrategy recommendedStrategy,
        out bool requiresDowngrade)
    {
        requiresDowngrade = false;

        if (manualStrategy != LinkCreationStrategy.Auto)
        {
            return backendService.Supports(sourceKind, manualStrategy) ? manualStrategy : LinkCreationStrategy.Auto;
        }

        if (backendService.Supports(sourceKind, recommendedStrategy))
        {
            return recommendedStrategy;
        }

        var fallbackStrategy = GetFallbackStrategy(sourceKind);
        if (backendService.Supports(sourceKind, fallbackStrategy))
        {
            requiresDowngrade = true;
            return fallbackStrategy;
        }

        return LinkCreationStrategy.Auto;
    }

    private async Task UpdateStoredTargetAsync(
        ManagedLinkRecord record,
        ManagedLinkTargetRecord target,
        Action<ManagedLinkTargetRecord> updateTarget)
    {
        var registry = await registryStorageService.LoadRegistryAsync();
        var storedRecord = FindRecord(registry, record.Id);
        var storedTarget = storedRecord is null ? null : FindTarget(storedRecord, target.Id, target.TargetPath);
        if (storedRecord is null || storedTarget is null)
        {
            return;
        }

        updateTarget(storedTarget);
        storedRecord.UpdatedAt = DateTimeOffset.Now;
        await registryStorageService.SaveRegistryAsync(registry);
    }

    private static void DeleteTargetEntity(LinkSourceKind sourceKind, ManagedLinkTargetRecord target)
    {
        if (sourceKind == LinkSourceKind.Directory || target.AppliedStrategy == LinkCreationStrategy.Junction)
        {
            Directory.Delete(target.TargetPath);
            return;
        }

        File.Delete(target.TargetPath);
    }

    private static ManagedLinkRecord? FindRecord(ManagedLinkRegistryDocument registry, string recordId)
    {
        return registry.Records.FirstOrDefault(item => string.Equals(item.Id, recordId, StringComparison.Ordinal));
    }

    private static ManagedLinkTargetRecord? FindTarget(ManagedLinkRecord record, string targetId, string targetPath)
    {
        return record.Targets.FirstOrDefault(item =>
            string.Equals(item.Id, targetId, StringComparison.Ordinal) ||
            string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
    }

    private static LinkCreationStrategy GetRecommendedStrategy(LinkSourceKind sourceKind)
    {
        return sourceKind switch
        {
            LinkSourceKind.File => LinkCreationStrategy.SymbolicLink,
            LinkSourceKind.Directory => LinkCreationStrategy.SymbolicLink,
            _ => LinkCreationStrategy.Auto,
        };
    }

    private static LinkCreationStrategy GetFallbackStrategy(LinkSourceKind sourceKind)
    {
        return sourceKind switch
        {
            LinkSourceKind.File => LinkCreationStrategy.HardLink,
            LinkSourceKind.Directory => LinkCreationStrategy.Junction,
            _ => LinkCreationStrategy.Auto,
        };
    }

    private static void MergeManagedRecords(ManagedLinkRegistryDocument registry, IEnumerable<ManagedLinkRecord> newRecords)
    {
        foreach (var newRecord in newRecords)
        {
            var existingRecord = registry.Records.FirstOrDefault(record => record.Id == newRecord.Id);
            if (existingRecord is null)
            {
                registry.Records.Add(newRecord);
                continue;
            }

            existingRecord.DisplayName = newRecord.DisplayName;
            existingRecord.SourcePath = newRecord.SourcePath;
            existingRecord.SourceKind = newRecord.SourceKind;
            existingRecord.PreferredStrategy = newRecord.PreferredStrategy;
            existingRecord.UpdatedAt = newRecord.UpdatedAt;

            foreach (var newTarget in newRecord.Targets)
            {
                var existingTarget = existingRecord.Targets.FirstOrDefault(target =>
                    string.Equals(target.Id, newTarget.Id, StringComparison.Ordinal) ||
                    string.Equals(target.TargetPath, newTarget.TargetPath, StringComparison.OrdinalIgnoreCase));

                if (existingTarget is null)
                {
                    existingRecord.Targets.Add(newTarget);
                    continue;
                }

                existingTarget.DisplayName = newTarget.DisplayName;
                existingTarget.TargetPath = newTarget.TargetPath;
                existingTarget.AppliedStrategy = newTarget.AppliedStrategy;
                existingTarget.State = newTarget.State;
                existingTarget.StatusReason = newTarget.StatusReason;
                existingTarget.LastCheckedAt = newTarget.LastCheckedAt;
            }
        }
    }

    private static bool HasInvalidPathChars(string path)
    {
        return string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
    }
}
