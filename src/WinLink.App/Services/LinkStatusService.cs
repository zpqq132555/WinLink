using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 基于当前文件系统实际状态刷新受管目标的健康结果。
/// </summary>
public sealed class LinkStatusService : ILinkStatusService
{
    /// <summary>
    /// 逐个检查指定记录下的所有目标，并写回最新状态与原因。
    /// </summary>
    public Task<ManagedLinkRecord> RefreshAsync(ManagedLinkRecord record)
    {
        foreach (var target in record.Targets)
        {
            var inspection = ManagedLinkTargetInspector.Inspect(record.SourceKind, record.SourcePath, target);
            target.State = inspection.State;
            target.StatusReason = inspection.Reason;
            target.LastCheckedAt = inspection.CheckedAt;
        }

        record.UpdatedAt = DateTimeOffset.Now;
        return Task.FromResult(record);
    }
}

internal static class ManagedLinkTargetInspector
{
    public static ManagedLinkInspectionResult Inspect(LinkSourceKind sourceKind, string sourcePath, ManagedLinkTargetRecord target)
    {
        return target.AppliedStrategy switch
        {
            LinkCreationStrategy.HardLink => InspectHardLink(sourcePath, target.TargetPath),
            LinkCreationStrategy.SymbolicLink => InspectSymbolicLink(sourceKind, sourcePath, target.TargetPath),
            LinkCreationStrategy.Junction => InspectJunction(sourcePath, target.TargetPath),
            _ => CreateInvalid("无法根据当前策略确认目标状态。请先手工处理，再决定是否重建或移除记录。", canDeleteSafely: false, entryExists: EntryExists(target.TargetPath)),
        };
    }

    private static ManagedLinkInspectionResult InspectHardLink(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return CreateDisconnected($"目标文件不存在：{targetPath}");
        }

        if (!File.Exists(sourcePath))
        {
            return CreateInvalid($"源文件不存在，无法安全确认硬链接是否仍与记录匹配：{sourcePath}。如需清理，请手工检查后使用“移除记录”。", canDeleteSafely: false, entryExists: true);
        }

        try
        {
            return TryGetFileIdentity(sourcePath, out var sourceIdentity) &&
                   TryGetFileIdentity(targetPath, out var targetIdentity) &&
                   sourceIdentity == targetIdentity
                ? CreateActive("目标与记录的硬链接源匹配，当前生效中。", canDeleteSafely: true)
                : CreateInvalid("目标存在，但已不再与记录源文件保持同一实体。已阻止严格删除，请手工处理或使用“移除记录”。", canDeleteSafely: false, entryExists: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateInvalid($"检查硬链接状态失败：{ex.Message}", canDeleteSafely: false, entryExists: true);
        }
    }

    private static ManagedLinkInspectionResult InspectSymbolicLink(LinkSourceKind sourceKind, string sourcePath, string targetPath)
    {
        return InspectReparsePoint(sourceKind == LinkSourceKind.Directory, sourcePath, targetPath, "符号链接");
    }

    private static ManagedLinkInspectionResult InspectJunction(string sourcePath, string targetPath)
    {
        return InspectReparsePoint(expectsDirectory: true, sourcePath, targetPath, "Junction");
    }

    private static ManagedLinkInspectionResult InspectReparsePoint(bool expectsDirectory, string sourcePath, string targetPath, string linkDisplayName)
    {
        try
        {
            if (!TryGetAttributes(targetPath, out var attributes))
            {
                return CreateDisconnected($"目标不存在：{targetPath}");
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory != expectsDirectory)
            {
                return CreateInvalid($"目标对象类型已变化，它不再是记录中的 {linkDisplayName}。请手工处理或使用“移除记录”。", canDeleteSafely: false, entryExists: true);
            }

            FileSystemInfo info = expectsDirectory
                ? new DirectoryInfo(targetPath)
                : new FileInfo(targetPath);
            var rawLinkTarget = info.LinkTarget;
            if (string.IsNullOrWhiteSpace(rawLinkTarget))
            {
                return CreateInvalid($"目标存在，但已不是记录中的 {linkDisplayName}。已阻止严格删除，请手工处理或使用“移除记录”。", canDeleteSafely: false, entryExists: true);
            }

            var normalizedExpectedSource = NormalizePath(sourcePath);
            var normalizedCurrentTarget = NormalizeLinkedTargetPath(targetPath, rawLinkTarget);
            if (!string.Equals(normalizedExpectedSource, normalizedCurrentTarget, StringComparison.OrdinalIgnoreCase))
            {
                return CreateInvalid($"目标当前指向 {normalizedCurrentTarget}，不再是记录中的源路径。已阻止严格删除，请手工处理或使用“移除记录”。", canDeleteSafely: false, entryExists: true);
            }

            var sourceExists = expectsDirectory ? Directory.Exists(sourcePath) : File.Exists(sourcePath);
            return sourceExists
                ? CreateActive($"目标与记录的 {linkDisplayName} 匹配，当前生效中。", canDeleteSafely: true)
                : CreateInvalid($"源路径已不存在，但当前 {linkDisplayName} 仍指向记录源。可以先执行“删除链接”，再决定是否重建。", canDeleteSafely: true, entryExists: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateInvalid($"检查链接状态失败：{ex.Message}", canDeleteSafely: false, entryExists: EntryExists(targetPath));
        }
    }

    private static ManagedLinkInspectionResult CreateActive(string reason, bool canDeleteSafely)
    {
        return new ManagedLinkInspectionResult(LinkTargetState.Active, reason, canDeleteSafely, EntryExists: true, CheckedAt: DateTimeOffset.Now);
    }

    private static ManagedLinkInspectionResult CreateInvalid(string reason, bool canDeleteSafely, bool entryExists)
    {
        return new ManagedLinkInspectionResult(LinkTargetState.Invalid, reason, canDeleteSafely, entryExists, DateTimeOffset.Now);
    }

    private static ManagedLinkInspectionResult CreateDisconnected(string reason)
    {
        return new ManagedLinkInspectionResult(LinkTargetState.Disconnected, reason, CanDeleteSafely: false, EntryExists: false, CheckedAt: DateTimeOffset.Now);
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static bool EntryExists(string path)
    {
        return TryGetAttributes(path, out _);
    }

    private static string NormalizeLinkedTargetPath(string targetPath, string rawLinkTarget)
    {
        var parentDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var combinedPath = Path.IsPathRooted(rawLinkTarget)
            ? rawLinkTarget
            : Path.Combine(parentDirectory, rawLinkTarget);
        return NormalizePath(combinedPath);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryGetFileIdentity(string path, out FileIdentity identity)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var info))
        {
            identity = default;
            return false;
        }

        identity = new FileIdentity(
            info.VolumeSerialNumber,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);
}

internal sealed record ManagedLinkInspectionResult(
    LinkTargetState State,
    string Reason,
    bool CanDeleteSafely,
    bool EntryExists,
    DateTimeOffset CheckedAt);
