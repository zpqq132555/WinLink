using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 提供目录镜像模式所需的快照、比较和真实拷贝能力。
/// </summary>
public static class DirectoryMirrorService
{
    /// <summary>
    /// 采集指定目录当前的快照基线。
    /// </summary>
    public static List<DirectorySnapshotEntry> CaptureSnapshot(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(rootPath);
        }

        var snapshot = new List<DirectorySnapshotEntry>();
        var rootDirectory = new DirectoryInfo(rootPath);

        foreach (var directory in rootDirectory.EnumerateDirectories("*", SearchOption.AllDirectories)
                     .OrderBy(directory => GetRelativePath(rootPath, directory.FullName), StringComparer.OrdinalIgnoreCase))
        {
            snapshot.Add(new DirectorySnapshotEntry
            {
                RelativePath = GetRelativePath(rootPath, directory.FullName),
                IsDirectory = true,
            });
        }

        foreach (var file in rootDirectory.EnumerateFiles("*", SearchOption.AllDirectories)
                     .OrderBy(file => GetRelativePath(rootPath, file.FullName), StringComparer.OrdinalIgnoreCase))
        {
            snapshot.Add(new DirectorySnapshotEntry
            {
                RelativePath = GetRelativePath(rootPath, file.FullName),
                IsDirectory = false,
                Length = file.Length,
                LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks,
            });
        }

        return snapshot;
    }

    /// <summary>
    /// 判断两份目录快照是否一致。
    /// </summary>
    public static bool SnapshotsEqual(IReadOnlyList<DirectorySnapshotEntry>? left, IReadOnlyList<DirectorySnapshotEntry>? right)
    {
        left ??= [];
        right ??= [];

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftEntry = left[index];
            var rightEntry = right[index];
            if (!string.Equals(leftEntry.RelativePath, rightEntry.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                leftEntry.IsDirectory != rightEntry.IsDirectory ||
                leftEntry.Length != rightEntry.Length ||
                leftEntry.LastWriteTimeUtcTicks != rightEntry.LastWriteTimeUtcTicks)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 复制一份可独立修改的快照，避免同一基线被多处共享引用。
    /// </summary>
    public static List<DirectorySnapshotEntry> CloneSnapshot(IReadOnlyList<DirectorySnapshotEntry>? snapshot)
    {
        if (snapshot is null || snapshot.Count == 0)
        {
            return [];
        }

        return snapshot
            .Select(entry => new DirectorySnapshotEntry
            {
                RelativePath = entry.RelativePath,
                IsDirectory = entry.IsDirectory,
                Length = entry.Length,
                LastWriteTimeUtcTicks = entry.LastWriteTimeUtcTicks,
            })
            .ToList();
    }

    /// <summary>
    /// 以真镜像语义将源目录复制到目标目录。
    /// </summary>
    public static void MirrorDirectory(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(sourcePath);
        }

        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        DeleteTargetPath(targetPath);
        Directory.CreateDirectory(targetPath);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetPath, GetRelativePath(sourcePath, directoryPath)));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePath(sourcePath, filePath);
            var destinationPath = Path.Combine(targetPath, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// 删除镜像目标路径上的当前实体。
    /// </summary>
    public static void DeleteTargetPath(string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive: true);
            return;
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
    }

    private static string GetRelativePath(string rootPath, string childPath)
    {
        return Path.GetRelativePath(rootPath, childPath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
