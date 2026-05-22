using System.IO;
using WinLink.App.Models;

namespace WinLink.App.Services;

/// <summary>
/// 提供环境变量展开与路径类型推断的默认实现。
/// </summary>
public sealed class PathEnvironmentService : IPathEnvironmentService
{
    /// <summary>
    /// 展开路径中的环境变量。
    /// </summary>
    public string Expand(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// 根据现有磁盘信息推断源对象类型，不存在时返回未知。
    /// </summary>
    public LinkSourceKind DetectSourceKind(string path)
    {
        var expanded = Expand(path);
        if (File.Exists(expanded))
        {
            return LinkSourceKind.File;
        }

        if (Directory.Exists(expanded))
        {
            return LinkSourceKind.Directory;
        }

        return LinkSourceKind.Unknown;
    }
}
