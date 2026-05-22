using System.Windows;

namespace WinLink.App.Dialogs;

/// <summary>
/// 承载最近执行历史摘要的辅助弹窗。
/// </summary>
public partial class ExecutionHistoryWindow : Window
{
    /// <summary>
    /// 创建执行历史弹窗。
    /// </summary>
    public ExecutionHistoryWindow(object dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
    }
}
