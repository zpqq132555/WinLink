using System.IO;

namespace WinLink.App.Infrastructure;

/// <summary>
/// 统一描述 WinLink 在本机上的持久化目录与文件位置。
/// </summary>
public sealed class AppDirectories
{
    /// <summary>
    /// 使用给定根目录创建一组固定的存储路径。
    /// </summary>
    /// <param name="rootDirectory">WinLink 数据根目录，通常位于 `AppData\Local\WinLink`。</param>
    public AppDirectories(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        RegistryFilePath = Path.Combine(rootDirectory, "registry.json");
        HistoryFilePath = Path.Combine(rootDirectory, "history.json");
        UiStateFilePath = Path.Combine(rootDirectory, "ui-state.json");
    }

    /// <summary>
    /// WinLink 的数据根目录。
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// 受管链接注册表文件路径。
    /// </summary>
    public string RegistryFilePath { get; }

    /// <summary>
    /// 执行历史文件路径。
    /// </summary>
    public string HistoryFilePath { get; }

    /// <summary>
    /// 窗口和工作台轻量状态文件路径。
    /// </summary>
    public string UiStateFilePath { get; }

    /// <summary>
    /// 根据当前用户环境计算默认存储位置。
    /// </summary>
    public static AppDirectories CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppDirectories(Path.Combine(localAppData, "WinLink"));
    }

    /// <summary>
    /// 确保根目录已存在，便于后续读写 JSON。
    /// </summary>
    public void EnsureStorageDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
