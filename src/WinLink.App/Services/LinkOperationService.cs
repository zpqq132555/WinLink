using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 负责完整校验、执行规划、目录镜像同步，以及受管记录的删除与重建。
/// </summary>
public sealed class LinkOperationService : ILinkOperationService
{
    private readonly ILinkBackendService backendService;
    private readonly IPathEnvironmentService pathEnvironmentService;
    private readonly IRegistryStorageService registryStorageService;
    private readonly ILinkTaskWorkbenchService workbenchService;

    /// <summary>
    /// 使用路径服务、工作台服务、持久化服务和后端链接服务构造执行器。
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

    /// <inheritdoc />
    public Task<LinkExecutionPlan> PlanExecutionAsync(IEnumerable<LinkTaskDraft> tasks)
    {
        var plan = new LinkExecutionPlan();

        foreach (var task in tasks)
        {
            foreach (var target in task.Targets)
            {
                var plannedTarget = BuildPlannedTarget(task, target);
                plan.Targets.Add(plannedTarget);

                if (plannedTarget.Mode == ManagedPathMode.Link && plannedTarget.RequiresDowngrade)
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

    /// <inheritdoc />
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
                issues.Add($"任务“{task.DisplayName}”的源类型已变化，请重新识别源类型。");
            }

            if (task.Mode == ManagedPathMode.DirectoryMirror && actualKind != LinkSourceKind.Directory)
            {
                issues.Add($"任务“{task.DisplayName}”只有目录源才能使用镜像模式。");
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

        foreach (var plannedTarget in plan.Targets.Where(item =>
                     item.Mode == ManagedPathMode.Link &&
                     item.PlannedStrategy == LinkCreationStrategy.Auto))
        {
            issues.Add($"任务“{plannedTarget.TaskDisplayName}”的目标“{plannedTarget.Target.DisplayName}”缺少可用的链接策略。");
        }

        return issues;
    }

    /// <inheritdoc />
    public async Task<LinkExecutionBatchResult> ExecuteAsync(LinkExecutionPlan plan, bool allowDowngrade)
    {
        if (plan.Downgrades.Count > 0 && !allowDowngrade)
        {
            throw new InvalidOperationException("存在未确认的降级项，不能直接执行。");
        }

        var registry = await registryStorageService.LoadRegistryAsync();
        var history = await registryStorageService.LoadHistoryAsync();
        var managedRecordsByTask = new Dictionary<string, ManagedLinkRecord>(StringComparer.Ordinal);
        var sourceSnapshots = new Dictionary<string, List<DirectorySnapshotEntry>>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
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
                var appliedStrategy = await ExecutePlannedTargetAsync(plannedTarget);
                historyEntry.SuccessCount++;

                var managedRecordKey = string.IsNullOrWhiteSpace(plannedTarget.ReusedManagedSourceRecordId)
                    ? plannedTarget.TaskId
                    : plannedTarget.ReusedManagedSourceRecordId;

                if (!managedRecordsByTask.TryGetValue(managedRecordKey, out var record))
                {
                    record = new ManagedLinkRecord
                    {
                        Id = managedRecordKey,
                        DisplayName = plannedTarget.TaskDisplayName,
                        SourcePath = plannedTarget.SourcePath,
                        SourceKind = plannedTarget.SourceKind,
                        Mode = plannedTarget.Mode,
                        PreferredStrategy = plannedTarget.RecommendedStrategy,
                    };
                    managedRecordsByTask.Add(managedRecordKey, record);
                }

                ApplyExecutionMetadata(plannedTarget, record, sourceSnapshots, now);
                record.Targets.Add(CreateManagedTargetRecord(plannedTarget, appliedStrategy, sourceSnapshots, now));
                record.UpdatedAt = now;
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

    /// <inheritdoc />
    public async Task<LinkExecutionBatchResult> SyncMirrorAsync(ManagedLinkRecord record, bool allowTargetOverwrite)
    {
        var registry = await registryStorageService.LoadRegistryAsync();
        var history = await registryStorageService.LoadHistoryAsync();
        var storedRecord = FindRecord(registry, record.Id) ?? record;

        if (storedRecord.Mode != ManagedPathMode.DirectoryMirror)
        {
            throw new InvalidOperationException("当前记录不是目录镜像模式。");
        }

        if (pathEnvironmentService.DetectSourceKind(storedRecord.SourcePath) != LinkSourceKind.Directory)
        {
            throw new InvalidOperationException($"源目录不存在，无法同步：{storedRecord.SourcePath}");
        }

        var sourceSnapshot = DirectoryMirrorService.CaptureSnapshot(storedRecord.SourcePath);
        foreach (var target in storedRecord.Targets)
        {
            if (!allowTargetOverwrite && HasMirrorTargetLocalChanges(target))
            {
                throw new InvalidOperationException($"目标“{target.DisplayName}”存在本地改动，请确认是否覆盖。");
            }
        }

        var now = DateTimeOffset.Now;
        var historyEntry = new ExecutionHistoryEntry
        {
            Summary = $"同步目录镜像：{storedRecord.DisplayName}",
        };

        foreach (var target in storedRecord.Targets)
        {
            try
            {
                DirectoryMirrorService.MirrorDirectory(storedRecord.SourcePath, target.TargetPath);
                target.LastSynchronizedSnapshot = DirectoryMirrorService.CloneSnapshot(sourceSnapshot);
                target.LastSynchronizedAt = now;
                target.LastCheckedAt = now;
                target.State = LinkTargetState.Active;
                target.StatusReason = "已按源目录完成镜像同步。";
                historyEntry.SuccessCount++;
            }
            catch (Exception ex)
            {
                target.LastCheckedAt = now;
                target.State = LinkTargetState.Warning;
                target.StatusReason = $"镜像同步失败：{ex.Message}";
                historyEntry.FailedCount++;
                historyEntry.FailureReasons.Add($"{target.TargetPath}：{ex.Message}");
            }
        }

        storedRecord.LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(sourceSnapshot);
        storedRecord.LastSynchronizedAt = now;
        storedRecord.UpdatedAt = now;

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

    /// <inheritdoc />
    public async Task<string?> DeleteAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
    {
        if (record.Mode == ManagedPathMode.DirectoryMirror)
        {
            if (!File.Exists(target.TargetPath) && !Directory.Exists(target.TargetPath))
            {
                return "目标已不存在，无需再次删除。你可以改用“移除记录”清理登记。";
            }

            DirectoryMirrorService.DeleteTargetPath(target.TargetPath);
            await UpdateStoredTargetAsync(record, target, storedTarget =>
            {
                storedTarget.State = LinkTargetState.Disconnected;
                storedTarget.StatusReason = "镜像目标已删除，可按需重新同步。";
                storedTarget.LastCheckedAt = DateTimeOffset.Now;
            });
            return null;
        }

        var inspection = ManagedLinkTargetInspector.Inspect(record.SourceKind, record.SourcePath, target);
        if (!inspection.CanDeleteSafely)
        {
            return inspection.EntryExists
                ? inspection.Reason
                : "目标已不存在，无需再次删除链接。你可以改用“移除记录”清理登记。";
        }

        DeleteLinkedTargetEntity(record.SourceKind, target);
        await UpdateStoredTargetAsync(record, target, storedTarget =>
        {
            storedTarget.State = LinkTargetState.Disconnected;
            storedTarget.StatusReason = "链接对象已删除，可按需重建。";
            storedTarget.LastCheckedAt = DateTimeOffset.Now;
        });
        return null;
    }

    /// <inheritdoc />
    public async Task<string?> RebuildAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
    {
        var registry = await registryStorageService.LoadRegistryAsync();
        var currentRecord = FindRecord(registry, record.Id) ?? record;
        var currentTarget = FindTarget(currentRecord, target.Id, target.TargetPath) ?? target;

        if (currentRecord.Mode == ManagedPathMode.DirectoryMirror)
        {
            var syncResult = await SyncMirrorAsync(currentRecord, allowTargetOverwrite: true);
            return syncResult.HistoryEntry.FailedCount > 0
                ? syncResult.HistoryEntry.FailureReasons.FirstOrDefault() ?? $"目标“{currentTarget.DisplayName}”同步失败。"
                : null;
        }

        var inspection = ManagedLinkTargetInspector.Inspect(currentRecord.SourceKind, currentRecord.SourcePath, currentTarget);
        if (inspection.EntryExists)
        {
            if (!inspection.CanDeleteSafely)
            {
                return $"当前目标无法安全重建：{inspection.Reason}";
            }

            DeleteLinkedTargetEntity(currentRecord.SourceKind, currentTarget);
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
            Mode = currentRecord.Mode,
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

    /// <inheritdoc />
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
        var requiresDowngrade = false;
        var recommendedStrategy = task.Mode == ManagedPathMode.DirectoryMirror
            ? LinkCreationStrategy.Auto
            : GetRecommendedStrategy(task.SourceKind);
        var plannedStrategy = task.Mode == ManagedPathMode.DirectoryMirror
            ? LinkCreationStrategy.Auto
            : ResolvePlannedStrategy(task.SourceKind, task.PreferredStrategy, recommendedStrategy, out requiresDowngrade);

        return new PlannedLinkTarget
        {
            TaskId = task.Id,
            TaskDisplayName = task.DisplayName,
            SourcePath = task.SourcePath,
            SourceKind = task.SourceKind,
            Mode = task.Mode,
            ReusedManagedSourceRecordId = task.ReusedManagedSourceRecordId,
            Target = target,
            RecommendedStrategy = recommendedStrategy,
            PlannedStrategy = plannedStrategy,
            RequiresDowngrade = task.Mode == ManagedPathMode.Link && requiresDowngrade,
        };
    }

    private async Task<LinkCreationStrategy> ExecutePlannedTargetAsync(PlannedLinkTarget plannedTarget)
    {
        if (plannedTarget.Mode == ManagedPathMode.DirectoryMirror)
        {
            DirectoryMirrorService.MirrorDirectory(plannedTarget.SourcePath, plannedTarget.Target.TargetPath);
            return LinkCreationStrategy.Auto;
        }

        return await CreateWithFallbackAsync(plannedTarget);
    }

    private static void ApplyExecutionMetadata(
        PlannedLinkTarget plannedTarget,
        ManagedLinkRecord record,
        IDictionary<string, List<DirectorySnapshotEntry>> sourceSnapshots,
        DateTimeOffset now)
    {
        if (plannedTarget.Mode != ManagedPathMode.DirectoryMirror)
        {
            return;
        }

        if (!sourceSnapshots.TryGetValue(plannedTarget.SourcePath, out var sourceSnapshot))
        {
            sourceSnapshot = DirectoryMirrorService.CaptureSnapshot(plannedTarget.SourcePath);
            sourceSnapshots[plannedTarget.SourcePath] = sourceSnapshot;
        }

        record.LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(sourceSnapshot);
        record.LastSynchronizedAt = now;
    }

    private static ManagedLinkTargetRecord CreateManagedTargetRecord(
        PlannedLinkTarget plannedTarget,
        LinkCreationStrategy appliedStrategy,
        IDictionary<string, List<DirectorySnapshotEntry>> sourceSnapshots,
        DateTimeOffset now)
    {
        var targetRecord = new ManagedLinkTargetRecord
        {
            Id = plannedTarget.Target.Id,
            DisplayName = plannedTarget.Target.DisplayName,
            TargetPath = plannedTarget.Target.TargetPath,
            AppliedStrategy = appliedStrategy,
            State = LinkTargetState.Active,
            StatusReason = plannedTarget.Mode == ManagedPathMode.DirectoryMirror
                ? "已完成首次镜像拷贝。"
                : "最近一次执行创建成功。",
            LastCheckedAt = now,
        };

        if (plannedTarget.Mode == ManagedPathMode.DirectoryMirror &&
            sourceSnapshots.TryGetValue(plannedTarget.SourcePath, out var sourceSnapshot))
        {
            targetRecord.LastSynchronizedSnapshot = DirectoryMirrorService.CloneSnapshot(sourceSnapshot);
            targetRecord.LastSynchronizedAt = now;
        }

        return targetRecord;
    }

    private static bool HasMirrorTargetLocalChanges(ManagedLinkTargetRecord target)
    {
        if (File.Exists(target.TargetPath))
        {
            return true;
        }

        if (!Directory.Exists(target.TargetPath))
        {
            return false;
        }

        var currentSnapshot = DirectoryMirrorService.CaptureSnapshot(target.TargetPath);
        return !DirectoryMirrorService.SnapshotsEqual(target.LastSynchronizedSnapshot, currentSnapshot);
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

    private static void DeleteLinkedTargetEntity(LinkSourceKind sourceKind, ManagedLinkTargetRecord target)
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

    private async Task<LinkCreationStrategy> CreateWithFallbackAsync(PlannedLinkTarget plannedTarget)
    {
        try
        {
            await backendService.CreateAsync(
                plannedTarget.SourceKind,
                plannedTarget.SourcePath,
                plannedTarget.Target.TargetPath,
                plannedTarget.PlannedStrategy);
            return plannedTarget.PlannedStrategy;
        }
        catch (Exception ex) when (ShouldRetryWithFallback(ex, plannedTarget))
        {
            var fallbackStrategy = GetFallbackStrategy(plannedTarget.SourceKind);
            await backendService.CreateAsync(
                plannedTarget.SourceKind,
                plannedTarget.SourcePath,
                plannedTarget.Target.TargetPath,
                fallbackStrategy);
            return fallbackStrategy;
        }
    }

    private bool ShouldRetryWithFallback(Exception ex, PlannedLinkTarget plannedTarget)
    {
        if (plannedTarget.PlannedStrategy != LinkCreationStrategy.SymbolicLink)
        {
            return false;
        }

        var fallbackStrategy = GetFallbackStrategy(plannedTarget.SourceKind);
        if (fallbackStrategy == LinkCreationStrategy.Auto || !backendService.Supports(plannedTarget.SourceKind, fallbackStrategy))
        {
            return false;
        }

        return ex is UnauthorizedAccessException || ContainsPrivilegeHint(ex.Message);
    }

    private static bool ContainsPrivilegeHint(string message)
    {
        return message.Contains("特权", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("privilege", StringComparison.OrdinalIgnoreCase);
    }

    private static void MergeManagedRecords(ManagedLinkRegistryDocument registry, IEnumerable<ManagedLinkRecord> newRecords)
    {
        foreach (var newRecord in newRecords)
        {
            var existingRecord = FindRecordForMerge(registry, newRecord);
            if (existingRecord is null)
            {
                registry.Records.Add(newRecord);
                continue;
            }

            existingRecord.DisplayName = newRecord.DisplayName;
            existingRecord.SourcePath = newRecord.SourcePath;
            existingRecord.SourceKind = newRecord.SourceKind;
            existingRecord.Mode = newRecord.Mode;
            existingRecord.PreferredStrategy = newRecord.PreferredStrategy;
            existingRecord.LastSynchronizedSourceSnapshot = DirectoryMirrorService.CloneSnapshot(newRecord.LastSynchronizedSourceSnapshot);
            existingRecord.LastSynchronizedAt = newRecord.LastSynchronizedAt;
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
                existingTarget.LastSynchronizedSnapshot = DirectoryMirrorService.CloneSnapshot(newTarget.LastSynchronizedSnapshot);
                existingTarget.LastSynchronizedAt = newTarget.LastSynchronizedAt;
                existingTarget.LastCheckedAt = newTarget.LastCheckedAt;
            }
        }
    }

    private static ManagedLinkRecord? FindRecordForMerge(ManagedLinkRegistryDocument registry, ManagedLinkRecord newRecord)
    {
        return registry.Records.FirstOrDefault(record => string.Equals(record.Id, newRecord.Id, StringComparison.Ordinal))
            ?? registry.Records.FirstOrDefault(record =>
                string.Equals(record.DisplayName, newRecord.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeManagedPath(record.SourcePath), NormalizeManagedPath(newRecord.SourcePath), StringComparison.OrdinalIgnoreCase) &&
                record.SourceKind == newRecord.SourceKind &&
                record.Mode == newRecord.Mode);
    }

    private static string NormalizeManagedPath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool HasInvalidPathChars(string path)
    {
        return string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
    }
}
