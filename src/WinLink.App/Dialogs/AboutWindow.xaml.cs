using System.Windows;

namespace WinLink.App.Dialogs;

/// <summary>
/// 展示 WinLink 产品说明的辅助弹窗。
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>
    /// 创建关于弹窗。
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
