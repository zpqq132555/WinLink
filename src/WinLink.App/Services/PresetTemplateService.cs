using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 提供预设真实路径预览与“转成普通任务”的实例化能力。
/// </summary>
public sealed class PresetTemplateService
{
    private readonly IPathEnvironmentService pathEnvironmentService;
    private readonly string workspaceRoot;

    /// <summary>
    /// 使用路径服务和工作区根目录创建预设服务。
    /// </summary>
    public PresetTemplateService(IPathEnvironmentService pathEnvironmentService, string workspaceRoot)
    {
        this.pathEnvironmentService = pathEnvironmentService;
        this.workspaceRoot = workspaceRoot;
    }

    /// <summary>
    /// 生成预设在当前环境下的真实预览。
    /// </summary>
    public PresetTemplatePreview BuildPreview(PresetTemplateDefinition preset)
    {
        return new PresetTemplatePreview
        {
            Name = preset.Name,
            ExpandedSourcePath = ExpandTemplatePath(preset.SourcePathTemplate),
            Targets = preset.Targets.Select(target =>
            {
                var expandedTargetPath = ExpandTemplatePath(target.TargetPathTemplate);
                var parentDirectory = Path.GetDirectoryName(expandedTargetPath);

                return new PresetTemplatePreviewTarget
                {
                    DisplayName = target.DisplayName,
                    ExpandedTargetPath = expandedTargetPath,
                    HasConflict = File.Exists(expandedTargetPath) || Directory.Exists(expandedTargetPath),
                    ParentDirectoryMissing = !string.IsNullOrWhiteSpace(parentDirectory) && !Directory.Exists(parentDirectory),
                };
            }).ToList(),
        };
    }

    /// <summary>
    /// 把预设实例化为工作台中的普通待执行任务。
    /// </summary>
    public LinkTaskDraft InstantiateTask(PresetTemplateDefinition preset)
    {
        var sourcePath = ExpandTemplatePath(preset.SourcePathTemplate);
        var task = new LinkTaskDraft
        {
            Id = preset.Id,
            DisplayName = preset.Name,
            SourcePath = sourcePath,
            SourceKind = pathEnvironmentService.DetectSourceKind(sourcePath),
        };

        foreach (var target in preset.Targets)
        {
            var draft = new LinkTargetDraft
            {
                DisplayName = target.DisplayName,
            };
            draft.ApplyFullPath(ExpandTemplatePath(target.TargetPathTemplate));
            task.Targets.Add(draft);
        }

        return task;
    }

    private string ExpandTemplatePath(string pathTemplate)
    {
        var workspaceExpanded = pathTemplate.Replace("%WORKSPACE%", workspaceRoot, StringComparison.OrdinalIgnoreCase);
        return pathEnvironmentService.Expand(workspaceExpanded);
    }
}
