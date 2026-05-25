using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.Tests;

public sealed class ExecutionResultMessageFormatterTests
{
    [Fact]
    public void Format_should_include_failure_reasons_when_execution_contains_failures()
    {
        var entry = new ExecutionHistoryEntry
        {
            SuccessCount = 0,
            SkippedCount = 1,
            FailedCount = 2,
            FailureReasons =
            [
                @"C:\Users\zhangpeng\.codex\AGENTS.md：客户端没有所需的特权。",
                @"C:\Users\zhangpeng\.claude\CLAUDE.md：客户端没有所需的特权。",
            ],
        };

        var message = ExecutionResultMessageFormatter.Format(entry);

        Assert.Contains("成功 0，跳过 1，失败 2", message, StringComparison.Ordinal);
        Assert.Contains("失败原因：", message, StringComparison.Ordinal);
        Assert.Contains(entry.FailureReasons[0], message, StringComparison.Ordinal);
        Assert.Contains(entry.FailureReasons[1], message, StringComparison.Ordinal);
    }
}
