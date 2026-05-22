using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 实现工作台中的源锁定、目标录入和编辑期轻量预检查。
/// </summary>
public sealed class LinkTaskWorkbenchService : ILinkTaskWorkbenchService
{
    private readonly IPathEnvironmentService pathEnvironmentService;

    /// <summary>
    /// 使用路径环境服务创建工作台服务。
    /// </summary>
    public LinkTaskWorkbenchService(IPathEnvironmentService pathEnvironmentService)
    {
        this.pathEnvironmentService = pathEnvironmentService;
    }

    /// <summary>
    /// 根据源路径更新任务，并自动锁定其源类型。
    /// </summary>
    public void ApplySource(LinkTaskDraft task, string sourcePath)
    {
        var expandedPath = pathEnvironmentService.Expand(sourcePath.Trim());
        task.SourcePath = expandedPath;
        task.SourceKind = pathEnvironmentService.DetectSourceKind(expandedPath);

        if (string.IsNullOrWhiteSpace(task.DisplayName))
        {
            task.DisplayName = GetSourceLeafName(task);
        }

        RunLightValidation(task);
    }

    /// <summary>
    /// 按“目录 + 名称”模式向任务添加目标，并自动套用源名称。
    /// </summary>
    public LinkTargetDraft AddTargetFromDirectory(LinkTaskDraft task, string directoryPath, string? targetName = null)
    {
        var expandedDirectory = pathEnvironmentService.Expand(directoryPath.Trim());
        var effectiveName = string.IsNullOrWhiteSpace(targetName) ? GetSourceLeafName(task) : targetName.Trim();

        var target = new LinkTargetDraft();
        target.ApplyDirectoryTarget(expandedDirectory, effectiveName);
        task.Targets.Add(target);
        RunLightValidation(task);
        return target;
    }

    /// <summary>
    /// 按完整路径模式向任务添加目标。
    /// </summary>
    public LinkTargetDraft AddTargetFromFullPath(LinkTaskDraft task, string fullPath)
    {
        var expandedPath = pathEnvironmentService.Expand(fullPath.Trim());
        var target = new LinkTargetDraft();
        target.ApplyFullPath(expandedPath);
        task.Targets.Add(target);
        RunLightValidation(task);
        return target;
    }

    /// <summary>
    /// 从任务中移除目标，并重新计算剩余目标的轻量预检查结果。
    /// </summary>
    public void RemoveTarget(LinkTaskDraft task, LinkTargetDraft target)
    {
        task.Targets.Remove(target);
        RunLightValidation(task);
    }

    /// <summary>
    /// 对单个任务执行编辑期轻量预检查，并把冲突或格式问题写回目标行。
    /// </summary>
    public void RunLightValidation(LinkTaskDraft task)
    {
        foreach (var target in task.Targets)
        {
            target.ValidationMessage = GetLightValidationMessage(task, target);
        }
    }

    private string GetSourceLeafName(LinkTaskDraft task)
    {
        if (string.IsNullOrWhiteSpace(task.SourcePath))
        {
            return "新任务";
        }

        var normalizedPath = task.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalizedPath);
    }

    private string? GetLightValidationMessage(LinkTaskDraft task, LinkTargetDraft target)
    {
        if (string.IsNullOrWhiteSpace(task.SourcePath))
        {
            return "请先选择源路径。";
        }

        if (string.IsNullOrWhiteSpace(target.TargetPath))
        {
            return "目标路径不能为空。";
        }

        if (HasInvalidPathChars(target.TargetPath))
        {
            return "目标路径格式无效。";
        }

        if (File.Exists(target.TargetPath) || Directory.Exists(target.TargetPath))
        {
            return "目标已存在，请确认是否冲突。";
        }

        return null;
    }

    private static bool HasInvalidPathChars(string path)
    {
        return path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
    }
}
