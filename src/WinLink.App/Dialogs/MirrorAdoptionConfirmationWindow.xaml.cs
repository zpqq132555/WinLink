using System.Windows;
using WinLink.App.Models;

namespace WinLink.App.Dialogs;

/// <summary>
/// 在执行前统一展示所有可直接采纳的目录镜像目标。
/// </summary>
public partial class MirrorAdoptionConfirmationWindow : Window
{
    /// <summary>
    /// 使用待采纳的镜像目标初始化确认窗口。
    /// </summary>
    public MirrorAdoptionConfirmationWindow(IEnumerable<LinkExecutionMirrorAdoptionItem> items)
    {
        InitializeComponent();
        AdoptionItemsGrid.ItemsSource = items.ToList();
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
