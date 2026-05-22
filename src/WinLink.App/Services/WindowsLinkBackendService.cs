using System.Diagnostics;
using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 使用当前 Windows 文件系统 API 创建真实链接对象。
/// </summary>
public sealed class WindowsLinkBackendService : ILinkBackendService
{
    /// <summary>
    /// 判断指定策略在当前源类型上是否属于受支持的组合。
    /// </summary>
    public bool Supports(LinkSourceKind sourceKind, LinkCreationStrategy strategy)
    {
        return sourceKind switch
        {
            LinkSourceKind.File => strategy is LinkCreationStrategy.SymbolicLink or LinkCreationStrategy.HardLink,
            LinkSourceKind.Directory => strategy is LinkCreationStrategy.SymbolicLink or LinkCreationStrategy.Junction,
            _ => false,
        };
    }

    /// <summary>
    /// 按给定策略创建一个目标链接。
    /// </summary>
    public Task CreateAsync(LinkSourceKind sourceKind, string sourcePath, string targetPath, LinkCreationStrategy strategy)
    {
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        switch (sourceKind)
        {
            case LinkSourceKind.File when strategy == LinkCreationStrategy.SymbolicLink:
                File.CreateSymbolicLink(targetPath, sourcePath);
                break;
            case LinkSourceKind.File when strategy == LinkCreationStrategy.HardLink:
                CreateHardLink(targetPath, sourcePath);
                break;
            case LinkSourceKind.Directory when strategy == LinkCreationStrategy.SymbolicLink:
                Directory.CreateSymbolicLink(targetPath, sourcePath);
                break;
            case LinkSourceKind.Directory when strategy == LinkCreationStrategy.Junction:
                CreateJunction(targetPath, sourcePath);
                break;
            default:
                throw new NotSupportedException($"不支持的创建组合：{sourceKind} / {strategy}");
        }

        return Task.CompletedTask;
    }

    private static void CreateJunction(string targetPath, string sourcePath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{targetPath}\" \"{sourcePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        });

        process!.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "创建 Junction 失败。" : error.Trim());
        }
    }

    private static void CreateHardLink(string targetPath, string sourcePath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /H \"{targetPath}\" \"{sourcePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        });

        process!.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "创建 HardLink 失败。" : error.Trim());
        }
    }
}
