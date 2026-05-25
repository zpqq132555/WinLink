using WinLink.App.Models;

namespace WinLink.App.Services;

public static class ExecutionResultMessageFormatter
{
    public static string Format(ExecutionHistoryEntry entry)
    {
        var summary = $"成功 {entry.SuccessCount}，跳过 {entry.SkippedCount}，失败 {entry.FailedCount}";
        if (entry.FailureReasons.Count == 0)
        {
            return summary;
        }

        return $"{summary}{Environment.NewLine}{Environment.NewLine}失败原因：{Environment.NewLine}{string.Join(Environment.NewLine, entry.FailureReasons)}";
    }
}
