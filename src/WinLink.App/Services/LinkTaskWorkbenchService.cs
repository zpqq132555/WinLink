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

    /// <inheritdoc />
    public void ApplySource(LinkTaskDraft task, string sourcePath)
    {
        var expandedPath = pathEnvironmentService.Expand(sourcePath.Trim());
        task.SourcePath = expandedPath;
        task.SourceKind = pathEnvironmentService.DetectSourceKind(expandedPath);

        if (task.SourceKind != LinkSourceKind.Directory)
        {
            task.Mode = ManagedPathMode.Link;
        }

        if (string.IsNullOrWhiteSpace(task.DisplayName))
        {
            task.DisplayName = GetSourceLeafName(task);
        }

        RunLightValidation(task);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public LinkTargetDraft AddTargetFromFullPath(LinkTaskDraft task, string fullPath)
    {
        var expandedPath = pathEnvironmentService.Expand(fullPath.Trim());
        var target = new LinkTargetDraft();
        target.ApplyFullPath(expandedPath);
        task.Targets.Add(target);
        RunLightValidation(task);
        return target;
    }

    /// <inheritdoc />
    public void RemoveTarget(LinkTaskDraft task, LinkTargetDraft target)
    {
        task.Targets.Remove(target);
        RunLightValidation(task);
    }

    /// <inheritdoc />
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

        if (task.Mode == ManagedPathMode.DirectoryMirror && task.SourceKind != LinkSourceKind.Directory)
        {
            return "只有目录源才能使用镜像模式。";
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
