namespace WinLink.App.Models;

/// <summary>
/// 表示内置预设模板的定义。
/// </summary>
public sealed class PresetTemplateDefinition
{
    /// <summary>
    /// 预设唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 预设名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 预设说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 预设源路径，允许保留环境变量表达式。
    /// </summary>
    public string SourcePathTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 预设目标映射。
    /// </summary>
    public List<PresetTemplateTarget> Targets { get; set; } = [];
}

/// <summary>
/// 表示预设中的单个目标定义。
/// </summary>
public sealed class PresetTemplateTarget
{
    /// <summary>
    /// 目标描述名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 目标路径模板。
    /// </summary>
    public string TargetPathTemplate { get; set; } = string.Empty;
}

/// <summary>
/// 表示单个预设在当前环境下展开后的预览结果。
/// </summary>
public sealed class PresetTemplatePreview
{
    /// <summary>
    /// 预设名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 展开后的真实源路径。
    /// </summary>
    public string ExpandedSourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 展开后的目标预览集合。
    /// </summary>
    public List<PresetTemplatePreviewTarget> Targets { get; set; } = [];
}

/// <summary>
/// 表示预设中某个目标在当前环境下的预览状态。
/// </summary>
public sealed class PresetTemplatePreviewTarget
{
    /// <summary>
    /// 目标显示名。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 展开后的真实目标路径。
    /// </summary>
    public string ExpandedTargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 该目标是否已与现有文件系统冲突。
    /// </summary>
    public bool HasConflict { get; set; }

    /// <summary>
    /// 该目标的父目录当前是否缺失。
    /// </summary>
    public bool ParentDirectoryMissing { get; set; }
}
