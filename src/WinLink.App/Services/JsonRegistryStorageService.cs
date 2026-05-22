using System.IO;
using System.Text.Json;
using WinLink.App.Infrastructure;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 基于 `AppData\Local\WinLink` 下 JSON 文件的默认持久化实现。
/// </summary>
public sealed class JsonRegistryStorageService : IRegistryStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly AppDirectories directories;

    /// <summary>
    /// 使用给定目录集合创建 JSON 存储服务。
    /// </summary>
    public JsonRegistryStorageService(AppDirectories directories)
    {
        this.directories = directories;
    }

    /// <summary>
    /// 读取受管链接注册表，不存在时返回默认空文档。
    /// </summary>
    public Task<ManagedLinkRegistryDocument> LoadRegistryAsync()
    {
        return LoadAsync(directories.RegistryFilePath, () => new ManagedLinkRegistryDocument());
    }

    /// <summary>
    /// 写入受管链接注册表。
    /// </summary>
    public Task SaveRegistryAsync(ManagedLinkRegistryDocument document)
    {
        document.SchemaVersion = ManagedLinkRegistryDocument.CurrentSchemaVersion;
        return SaveAsync(directories.RegistryFilePath, document);
    }

    /// <summary>
    /// 读取执行历史，不存在时返回默认空文档。
    /// </summary>
    public Task<ExecutionHistoryDocument> LoadHistoryAsync()
    {
        return LoadAsync(directories.HistoryFilePath, () => new ExecutionHistoryDocument());
    }

    /// <summary>
    /// 写入执行历史。
    /// </summary>
    public Task SaveHistoryAsync(ExecutionHistoryDocument document)
    {
        document.SchemaVersion = ExecutionHistoryDocument.CurrentSchemaVersion;
        return SaveAsync(directories.HistoryFilePath, document);
    }

    /// <summary>
    /// 读取 UI 状态，不存在时返回默认空文档。
    /// </summary>
    public Task<WorkspaceUiStateDocument> LoadUiStateAsync()
    {
        return LoadAsync(directories.UiStateFilePath, () => new WorkspaceUiStateDocument());
    }

    /// <summary>
    /// 写入 UI 状态。
    /// </summary>
    public Task SaveUiStateAsync(WorkspaceUiStateDocument document)
    {
        document.SchemaVersion = WorkspaceUiStateDocument.CurrentSchemaVersion;
        return SaveAsync(directories.UiStateFilePath, document);
    }

    private async Task<TDocument> LoadAsync<TDocument>(string filePath, Func<TDocument> factory)
    {
        if (!File.Exists(filePath))
        {
            return factory();
        }

        await using var stream = File.OpenRead(filePath);
        var document = await JsonSerializer.DeserializeAsync<TDocument>(stream, SerializerOptions);
        return document ?? factory();
    }

    private async Task SaveAsync<TDocument>(string filePath, TDocument document)
    {
        directories.EnsureStorageDirectory();
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions);
    }
}
