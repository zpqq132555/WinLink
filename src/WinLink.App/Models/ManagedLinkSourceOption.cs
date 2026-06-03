namespace WinLink.App.Models;

/// <summary>
/// 表示“新建链接”中可直接复用的已连接源候选。
/// </summary>
public sealed class ManagedLinkSourceOption
{
    /// <summary>
    /// 当前候选对应的受管源记录标识。
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// 候选源的显示名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 候选源的真实源路径。
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 候选源的类型。
    /// </summary>
    public LinkSourceKind SourceKind { get; set; }

    /// <summary>
    /// 候选源默认偏好的创建策略。
    /// </summary>
    public LinkCreationStrategy PreferredStrategy { get; set; } = LinkCreationStrategy.Auto;

    /// <summary>
    /// 候选源当前使用的受管模式。
    /// </summary>
    public ManagedPathMode Mode { get; set; } = ManagedPathMode.Link;

    /// <summary>
    /// 下拉列表中实际展示给用户的文案。
    /// </summary>
    public string DisplayLabel { get; set; } = string.Empty;
}
