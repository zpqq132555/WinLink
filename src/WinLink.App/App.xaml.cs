using System.Windows;
using WinLink.App.Infrastructure;
using WinLink.App.Services;
using WinLink.App.ViewModels;

namespace WinLink.App;

/// <summary>
/// 负责应用启动时初始化基础目录、服务和主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 启动应用并显示主窗口。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var directories = AppDirectories.CreateDefault();
        directories.EnsureStorageDirectory();
        var pathEnvironmentService = new PathEnvironmentService();
        var workbenchService = new LinkTaskWorkbenchService(pathEnvironmentService);
        var registryStorageService = new JsonRegistryStorageService(directories);
        var linkStatusService = new LinkStatusService();
        var linkOperationService = new LinkOperationService(
            pathEnvironmentService,
            workbenchService,
            registryStorageService,
            new WindowsLinkBackendService());
        var presetTemplateService = new PresetTemplateService(pathEnvironmentService, Environment.CurrentDirectory);
        var shellViewModel = new ShellViewModel(
            directories,
            pathEnvironmentService,
            workbenchService,
            linkOperationService,
            linkStatusService,
            presetTemplateService,
            registryStorageService);
        var mainWindow = new MainWindow(shellViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
