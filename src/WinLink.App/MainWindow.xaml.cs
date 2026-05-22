using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinLink.App.Dialogs;
using WinLink.App.ViewModels;

namespace WinLink.App;

/// <summary>
/// WinLink 的主桌面窗口，承载三个核心工作区和全局菜单入口。
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel viewModel;

    /// <summary>
    /// 使用主窗口视图模型创建桌面壳层。
    /// </summary>
    public MainWindow(ShellViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RefreshManagedLinksMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.RefreshManagedLinksAsync();
    }

    private void ExecutionHistoryMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new ExecutionHistoryWindow(viewModel)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void DowngradePreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        var plan = viewModel.BuildExecutionPlanAsync().GetAwaiter().GetResult();
        var window = new DowngradeConfirmationWindow(plan.Downgrades)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void AddPendingTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.AddPendingTask();
    }

    private void RemovePendingTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.RemoveSelectedTask();
    }

    private void BrowseFileSourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择文件源",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.ApplySourceToSelectedTask(dialog.FileName);
        }
    }

    private void BrowseDirectorySourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择目录源",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.ApplySourceToSelectedTask(dialog.FolderName);
        }
    }

    private void BrowseTargetDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择目标目录",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.DirectoryTargetDirectoryInput = dialog.FolderName;
        }
    }

    private void DetectSourceTypeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedTask is null)
        {
            return;
        }

        viewModel.ApplySourceToSelectedTask(viewModel.SelectedTask.SourcePath);
    }

    private void SourcePathTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedTask is null)
        {
            return;
        }

        viewModel.ApplySourceToSelectedTask(viewModel.SelectedTask.SourcePath);
    }

    private void AddDirectoryTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.AddDirectoryTargetToSelectedTask();
    }

    private void AddFullPathTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.AddFullPathTargetToSelectedTask();
    }

    private void RemoveSelectedTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.RemoveSelectedTarget();
    }

    private void RefreshPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.RefreshSelectedTaskLightValidation();
    }

    private void ConvertPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.ConvertSelectedPresetToPendingTask();
    }

    private async void ApplyPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var issues = await viewModel.ValidateSelectedPresetAsync();
        if (issues.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, issues),
                "请先修复预设校验问题",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var plan = await viewModel.BuildSelectedPresetExecutionPlanAsync();
        var allowDowngrade = true;
        if (plan.Downgrades.Count > 0)
        {
            var confirmationWindow = new DowngradeConfirmationWindow(plan.Downgrades)
            {
                Owner = this,
            };
            allowDowngrade = confirmationWindow.ShowDialog() == true;
        }

        if (!allowDowngrade)
        {
            viewModel.ShowStatus("已取消本次预设应用。");
            return;
        }

        var result = await viewModel.ApplySelectedPresetAsync(plan, allowDowngrade);
        MessageBox.Show(
            this,
            $"成功 {result.HistoryEntry.SuccessCount}，跳过 {result.HistoryEntry.SkippedCount}，失败 {result.HistoryEntry.FailedCount}",
            "预设应用完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ValidateAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        var issues = await viewModel.ValidateAllPendingTasksAsync();
        if (issues.Count == 0)
        {
            MessageBox.Show(this, "完整校验通过，可以继续进入后续执行流程。", "WinLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, issues),
            "完整校验发现问题",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async void ExecuteAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        var issues = await viewModel.ValidateAllPendingTasksAsync();
        if (issues.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, issues),
                "请先修复校验问题",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var plan = await viewModel.BuildExecutionPlanAsync();
        var allowDowngrade = true;
        if (plan.Downgrades.Count > 0)
        {
            var confirmationWindow = new DowngradeConfirmationWindow(plan.Downgrades)
            {
                Owner = this,
            };
            allowDowngrade = confirmationWindow.ShowDialog() == true;
        }

        if (!allowDowngrade)
        {
            viewModel.ShowStatus("已取消本次执行。");
            return;
        }

        var result = await viewModel.ExecutePendingTasksAsync(plan, allowDowngrade);
        MessageBox.Show(
            this,
            $"成功 {result.HistoryEntry.SuccessCount}，跳过 {result.HistoryEntry.SkippedCount}，失败 {result.HistoryEntry.FailedCount}",
            "批量执行完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TargetsDataGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() => viewModel.RefreshSelectedTaskLightValidation());
    }

    private async void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        if (ReferenceEquals(MainTabControl.SelectedItem, ManagedLinksTabItem))
        {
            await viewModel.RefreshManagedLinksAsync(autoTriggered: true);
        }
    }

    private void ManagedLinkDetailsExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        viewModel.SetManagedLinkExpanded(viewModel.SelectedManagedLink, true);
    }

    private void ManagedLinkDetailsExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        viewModel.SetManagedLinkExpanded(viewModel.SelectedManagedLink, false);
    }

    private async void DeleteManagedTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var reason = await viewModel.DeleteManagedTargetAsync(viewModel.SelectedManagedTarget);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, reason, "已阻止删除链接", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RebuildManagedTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var reason = await viewModel.RebuildManagedTargetAsync(viewModel.SelectedManagedTarget);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, reason, "重建链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RemoveManagedTargetRecordButton_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.RemoveManagedTargetRecordAsync(viewModel.SelectedManagedTarget);
    }
}
