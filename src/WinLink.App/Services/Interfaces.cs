using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 负责路径变量展开与基础路径语义判断。
/// </summary>
public interface IPathEnvironmentService
{
    /// <summary>
    /// 展开路径中的环境变量占位符。
    /// </summary>
    string Expand(string path);

    /// <summary>
    /// 根据磁盘现状推断源对象类型。
    /// </summary>
    LinkSourceKind DetectSourceKind(string path);
}

/// <summary>
/// 负责读取和写入 WinLink 的 JSON 文档。
/// </summary>
public interface IRegistryStorageService
{
    /// <summary>
    /// 读取受管链接注册表。
    /// </summary>
    Task<ManagedLinkRegistryDocument> LoadRegistryAsync();

    /// <summary>
    /// 写入受管链接注册表。
    /// </summary>
    Task SaveRegistryAsync(ManagedLinkRegistryDocument document);

    /// <summary>
    /// 读取执行历史。
    /// </summary>
    Task<ExecutionHistoryDocument> LoadHistoryAsync();

    /// <summary>
    /// 写入执行历史。
    /// </summary>
    Task SaveHistoryAsync(ExecutionHistoryDocument document);

    /// <summary>
    /// 读取 UI 轻量状态。
    /// </summary>
    Task<WorkspaceUiStateDocument> LoadUiStateAsync();

    /// <summary>
    /// 写入 UI 轻量状态。
    /// </summary>
    Task SaveUiStateAsync(WorkspaceUiStateDocument document);
}

/// <summary>
/// 抽象 `新建链接` 工作台中的任务编辑、目标录入与轻量预检查行为。
/// </summary>
public interface ILinkTaskWorkbenchService
{
    /// <summary>
    /// 根据源路径更新任务，并自动锁定源类型。
    /// </summary>
    void ApplySource(LinkTaskDraft task, string sourcePath);

    /// <summary>
    /// 按“目录 + 名称”模式向任务添加目标。
    /// </summary>
    LinkTargetDraft AddTargetFromDirectory(LinkTaskDraft task, string directoryPath, string? targetName = null);

    /// <summary>
    /// 按完整路径模式向任务添加目标。
    /// </summary>
    LinkTargetDraft AddTargetFromFullPath(LinkTaskDraft task, string fullPath);

    /// <summary>
    /// 从任务中移除目标。
    /// </summary>
    void RemoveTarget(LinkTaskDraft task, LinkTargetDraft target);

    /// <summary>
    /// 对单个任务执行编辑期轻量预检查。
    /// </summary>
    void RunLightValidation(LinkTaskDraft task);
}

/// <summary>
/// 抽象具体的 Windows 链接创建后端，便于在执行逻辑与真实文件系统之间解耦。
/// </summary>
public interface ILinkBackendService
{
    /// <summary>
    /// 判断指定源类型是否支持某种链接策略。
    /// </summary>
    bool Supports(LinkSourceKind sourceKind, LinkCreationStrategy strategy);

    /// <summary>
    /// 按给定策略创建单个目标链接。
    /// </summary>
    Task CreateAsync(LinkSourceKind sourceKind, string sourcePath, string targetPath, LinkCreationStrategy strategy);
}

/// <summary>
/// 抽象链接创建、重建与严格删除等动作。
/// </summary>
public interface ILinkOperationService
{
    /// <summary>
    /// 对批量任务生成执行计划，并汇总需要统一确认的降级项。
    /// </summary>
    Task<LinkExecutionPlan> PlanExecutionAsync(IEnumerable<LinkTaskDraft> tasks);

    /// <summary>
    /// 对批量任务执行完整校验。
    /// </summary>
    Task<IReadOnlyList<string>> ValidateAsync(IEnumerable<LinkTaskDraft> tasks);

    /// <summary>
    /// 执行批量创建。
    /// </summary>
    Task<LinkExecutionBatchResult> ExecuteAsync(LinkExecutionPlan plan, bool allowDowngrade);

    /// <summary>
    /// 对单个目标执行严格删除。
    /// </summary>
    Task<string?> DeleteAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target);

    /// <summary>
    /// 鍩轰簬宸茶褰曠殑婧愪笌鐩爣鏄犲皠閲嶆柊鍒涘缓鍗曚釜鐩爣閾炬帴銆?
    /// </summary>
    Task<string?> RebuildAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target);

    /// <summary>
    /// 浠呬粠鍙楃娉ㄥ唽琛ㄤ腑绉婚櫎鏌愪釜鐩爣璁板綍锛屼笉瑙﹀姩褰撳墠纾佺洏瀹炰綋銆?
    /// </summary>
    Task RemoveRecordAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target);
}

/// <summary>
/// 抽象受管目标的状态检查行为。
/// </summary>
public interface ILinkStatusService
{
    /// <summary>
    /// 刷新指定记录下所有目标的状态。
    /// </summary>
    Task<ManagedLinkRecord> RefreshAsync(ManagedLinkRecord record);
}
