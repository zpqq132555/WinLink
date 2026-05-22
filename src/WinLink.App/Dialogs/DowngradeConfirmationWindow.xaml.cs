using System.Windows;
using WinLink.App.Models;

namespace WinLink.App.Dialogs;

/// <summary>
/// 在执行前统一展示所有需要从推荐策略降级的目标项。
/// </summary>
public partial class DowngradeConfirmationWindow : Window
{
    /// <summary>
    /// 使用降级项列表创建确认窗口。
    /// </summary>
    public DowngradeConfirmationWindow(IEnumerable<LinkExecutionDowngradeItem> items)
    {
        InitializeComponent();
        DowngradeItemsGrid.ItemsSource = items.ToList();
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
