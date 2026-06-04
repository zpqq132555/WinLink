using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinLink.App.Dialogs;
using WinLink.App.Models;
using WinLink.App.Services;
using WinLink.App.ViewModels;

namespace WinLink.App;

/// <summary>
/// WinLink 的主桌面窗口，承载三个核心工作区和全局菜单入口。
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel viewModel;

    /// <summary>
    /// 使用主窗口视图模型创建界面壳层。
    /// </summary>
    public MainWindow(ShellViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.RefreshManagedLinksAsync(autoTriggered: true);
        await PromptMirrorSyncIfNeededAsync();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RefreshManagedLinksMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.RefreshManagedLinksAsync();
        await PromptMirrorSyncIfNeededAsync();
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

    private void ClearReusableManagedSourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        viewModel.ClearSelectedTaskReusableManagedSource();
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

        var allowMirrorAdoption = true;
        if (plan.MirrorAdoptions.Count > 0)
        {
            var confirmationWindow = new MirrorAdoptionConfirmationWindow(plan.MirrorAdoptions)
            {
                Owner = this,
            };
            allowMirrorAdoption = confirmationWindow.ShowDialog() == true;
        }

        if (!allowMirrorAdoption)
        {
            viewModel.ShowStatus("已取消本次预设应用。");
            return;
        }

        var result = await viewModel.ApplySelectedPresetAsync(plan, allowDowngrade, allowMirrorAdoption);
        MessageBox.Show(
            this,
            ExecutionResultMessageFormatter.Format(result.HistoryEntry),
            "预设应用完成",
            MessageBoxButton.OK,
            result.HistoryEntry.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private async void ValidateAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        var issues = await viewModel.ValidateAllPendingTasksAsync();
        if (issues.Count == 0)
        {
            MessageBox.Show(this, "完整校验通过，可以继续进入执行流程。", "WinLink", MessageBoxButton.OK, MessageBoxImage.Information);
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

        var allowMirrorAdoption = true;
        if (plan.MirrorAdoptions.Count > 0)
        {
            var confirmationWindow = new MirrorAdoptionConfirmationWindow(plan.MirrorAdoptions)
            {
                Owner = this,
            };
            allowMirrorAdoption = confirmationWindow.ShowDialog() == true;
        }

        if (!allowMirrorAdoption)
        {
            viewModel.ShowStatus("已取消本次执行。");
            return;
        }

        var result = await viewModel.ExecutePendingTasksAsync(plan, allowDowngrade, allowMirrorAdoption);
        MessageBox.Show(
            this,
            ExecutionResultMessageFormatter.Format(result.HistoryEntry),
            "批量执行完成",
            MessageBoxButton.OK,
            result.HistoryEntry.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
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
            await PromptMirrorSyncIfNeededAsync();
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
            MessageBox.Show(this, reason, "已阻止删除目标", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RebuildManagedTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var reason = await viewModel.RebuildManagedTargetAsync(viewModel.SelectedManagedTarget);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, reason, "重建目标失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SyncManagedMirrorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanSyncSelectedManagedMirror)
        {
            return;
        }

        await SyncCurrentMirrorRecordAsync();
    }

    private async void RemoveManagedTargetRecordButton_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.RemoveManagedTargetRecordAsync(viewModel.SelectedManagedTarget);
    }

    private void ManagedTargetDetailsExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        SetManagedTargetRowDetailsVisibility(sender, Visibility.Visible);
    }

    private void ManagedTargetDetailsExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        SetManagedTargetRowDetailsVisibility(sender, Visibility.Collapsed);
    }

    private async Task PromptMirrorSyncIfNeededAsync()
    {
        var recordIds = viewModel.GetMirrorRecordsNeedingSync()
            .Select(record => record.Id)
            .ToList();
        foreach (var recordId in recordIds)
        {
            var currentRecord = viewModel.ManagedLinks.FirstOrDefault(record => string.Equals(record.Id, recordId, StringComparison.Ordinal));
            if (currentRecord is null)
            {
                continue;
            }

            viewModel.SelectedManagedLink = currentRecord;
            var result = MessageBox.Show(
                this,
                BuildMirrorPromptMessage(currentRecord),
                "检测到目录镜像变更",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                continue;
            }

            await SyncCurrentMirrorRecordAsync();
        }
    }

    private async Task SyncCurrentMirrorRecordAsync()
    {
        var selectedRecord = viewModel.SelectedManagedLink;
        if (selectedRecord is null || selectedRecord.Mode != ManagedPathMode.DirectoryMirror)
        {
            return;
        }

        var allowOverwrite = true;
        if (selectedRecord.Targets.Any(target => target.State == LinkTargetState.Warning))
        {
            var overwriteConfirmation = MessageBox.Show(
                this,
                "部分目标目录存在本地改动。继续同步会覆盖这些改动，并清理目标中源目录不存在的额外内容。是否继续？",
                "确认覆盖目标改动",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (overwriteConfirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            var result = await viewModel.SyncSelectedManagedMirrorAsync(allowOverwrite);
            if (result is null)
            {
                return;
            }

            MessageBox.Show(
                this,
                ExecutionResultMessageFormatter.Format(result.HistoryEntry),
                "目录镜像同步完成",
                MessageBoxButton.OK,
                result.HistoryEntry.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "目录镜像同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string BuildMirrorPromptMessage(ManagedLinkRecord record)
    {
        if (record.Targets.Any(target => target.State == LinkTargetState.Warning))
        {
            return $"检测到源目录“{record.DisplayName}”发生变化，且部分目标存在本地改动。是否现在同步到所有目标目录？";
        }

        return $"检测到源目录“{record.DisplayName}”发生变化。是否现在同步到所有目标目录？";
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T typed)
            {
                return typed;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static void SetManagedTargetRowDetailsVisibility(object sender, Visibility visibility)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(source);
        if (row is null)
        {
            return;
        }

        row.DetailsVisibility = visibility;
    }
}
