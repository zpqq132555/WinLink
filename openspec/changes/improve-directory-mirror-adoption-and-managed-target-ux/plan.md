# Directory Mirror Adoption And Managed Target UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 WinLink 在目录镜像目标已存在且内容一致时支持“仅登记受管记录”，并同步修正“已连接”页详情展开与移除记录后的选中恢复行为。

**Architecture:** 这次改动以 `LinkOperationService` 为中心扩展目录镜像计划与执行分支，在 `PresetTemplateService` 和 `LinkTaskWorkbenchService` 上游统一暴露“可采纳”语义，并通过 `ShellViewModel` 与 `MainWindow.xaml` 收敛已连接页的交互行为。测试继续放在 `tests/WinLink.App.Tests` 中，分别覆盖服务层分支、ViewModel 列表恢复和界面绑定行为。

**Tech Stack:** C# / .NET 8 WPF、xUnit、OpenSpec、现有 `WinLink.App` 服务与 ViewModel 架构。

---

### Task 1: 建模目录镜像“可采纳”状态

**Files:**
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Models\LinkModels.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\LinkOperationService.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\Interfaces.cs`
- Test: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\LinkExecutionServiceTests.cs`

- [ ] **Step 1: 为执行计划补充镜像采纳项和目标判定模型**

```csharp
public enum MirrorTargetDisposition
{
    CreateNew = 0,
    AdoptExisting = 1,
    Conflict = 2,
}

public sealed class LinkExecutionMirrorAdoptionItem
{
    public string TaskDisplayName { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
}
```

并在 `LinkExecutionPlan` 中新增：

```csharp
public List<LinkExecutionMirrorAdoptionItem> MirrorAdoptions { get; set; } = [];
```

- [ ] **Step 2: 在 `BuildPlannedTarget` 结果中记录目录镜像目标的处置方式**

在 `PlannedLinkTarget` 中新增：

```csharp
public MirrorTargetDisposition MirrorDisposition { get; set; }
```

在 `LinkOperationService.BuildPlannedTarget(...)` 中为目录镜像目标设置默认值：

```csharp
MirrorDisposition = task.Mode == ManagedPathMode.DirectoryMirror
    ? DetermineMirrorDisposition(task.SourcePath, target.TargetPath)
    : MirrorTargetDisposition.CreateNew,
```

- [ ] **Step 3: 先写失败测试，定义“可采纳目标进入计划”的行为**

在 `LinkExecutionServiceTests.cs` 新增测试骨架：

```csharp
[Fact]
public async Task PlanExecutionAsync_should_collect_adoptable_existing_directory_mirrors()
{
    var tempRoot = CreateTemporaryDirectory();
    var sourcePath = Path.Combine(tempRoot, "skills");
    var targetPath = Path.Combine(tempRoot, "mirror", "skills");
    Directory.CreateDirectory(sourcePath);
    File.WriteAllText(Path.Combine(sourcePath, "config.json"), "{ }");
    DirectoryMirrorService.MirrorDirectory(sourcePath, targetPath);

    var task = new LinkTaskDraft
    {
        DisplayName = "skills 镜像",
        SourcePath = sourcePath,
        SourceKind = LinkSourceKind.Directory,
        Mode = ManagedPathMode.DirectoryMirror,
    };
    var target = new LinkTargetDraft();
    target.ApplyFullPath(targetPath);
    task.Targets.Add(target);

    var service = CreateExecutionService(tempRoot, new FakeLinkBackendService());
    var plan = await service.PlanExecutionAsync([task]);

    Assert.Single(plan.MirrorAdoptions);
    Assert.Equal(0, plan.Downgrades.Count);
    Assert.Equal(MirrorTargetDisposition.AdoptExisting, plan.Targets[0].MirrorDisposition);
}
```

- [ ] **Step 4: 运行单测确认当前行为失败**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~PlanExecutionAsync_should_collect_adoptable_existing_directory_mirrors"
```

Expected: FAIL，原因应是 `MirrorAdoptions`/`MirrorDisposition` 尚不存在，或计划结果仍把目标视为普通已存在目标。

- [ ] **Step 5: 实现 `DetermineMirrorDisposition` 最小逻辑**

在 `LinkOperationService` 中新增私有方法：

```csharp
private MirrorTargetDisposition DetermineMirrorDisposition(string sourcePath, string targetPath)
{
    if (File.Exists(targetPath))
    {
        return MirrorTargetDisposition.Conflict;
    }

    if (!Directory.Exists(targetPath))
    {
        return MirrorTargetDisposition.CreateNew;
    }

    var sourceSnapshot = DirectoryMirrorService.CaptureSnapshot(sourcePath);
    var targetSnapshot = DirectoryMirrorService.CaptureSnapshot(targetPath);
    return DirectoryMirrorService.SnapshotsEqual(sourceSnapshot, targetSnapshot)
        ? MirrorTargetDisposition.AdoptExisting
        : MirrorTargetDisposition.Conflict;
}
```

并在 `PlanExecutionAsync` 中把 `AdoptExisting` 目标加入 `MirrorAdoptions`。

- [ ] **Step 6: 重跑测试确认计划阶段通过**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~PlanExecutionAsync_should_collect_adoptable_existing_directory_mirrors"
```

Expected: PASS

- [ ] **Step 7: 提交计划建模改动**

```bash
git add src/WinLink.App/Models/LinkModels.cs src/WinLink.App/Services/Interfaces.cs src/WinLink.App/Services/LinkOperationService.cs tests/WinLink.App.Tests/LinkExecutionServiceTests.cs
git commit -m "feat: model adoptable directory mirrors in execution plans"
```

### Task 2: 实现目录镜像采纳执行与确认流

**Files:**
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\Interfaces.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\LinkOperationService.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\ViewModels\ShellViewModel.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\MainWindow.xaml.cs`
- Create: `E:\project\CSharpProject\winlink\src\WinLink.App\Dialogs\MirrorAdoptionConfirmationWindow.xaml`
- Create: `E:\project\CSharpProject\winlink\src\WinLink.App\Dialogs\MirrorAdoptionConfirmationWindow.xaml.cs`
- Test: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\LinkExecutionServiceTests.cs`

- [ ] **Step 1: 先写失败测试，定义“仅登记不重拷贝”的执行结果**

在 `LinkExecutionServiceTests.cs` 新增：

```csharp
[Fact]
public async Task ExecuteAsync_should_adopt_existing_directory_mirror_and_persist_snapshots()
{
    var tempRoot = CreateTemporaryDirectory();
    var sourcePath = Path.Combine(tempRoot, "skills");
    var targetPath = Path.Combine(tempRoot, "mirror", "skills");
    Directory.CreateDirectory(sourcePath);
    File.WriteAllText(Path.Combine(sourcePath, "config.json"), "{ }");
    DirectoryMirrorService.MirrorDirectory(sourcePath, targetPath);

    var task = new LinkTaskDraft
    {
        DisplayName = "skills 镜像",
        SourcePath = sourcePath,
        SourceKind = LinkSourceKind.Directory,
        Mode = ManagedPathMode.DirectoryMirror,
    };
    var target = new LinkTargetDraft();
    target.ApplyFullPath(targetPath);
    task.Targets.Add(target);

    var service = CreateExecutionService(tempRoot, new FakeLinkBackendService());
    var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
    var plan = await service.PlanExecutionAsync([task]);
    var result = await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);
    var record = (await storage.LoadRegistryAsync()).Records.Single();

    Assert.Equal(1, result.HistoryEntry.SuccessCount);
    Assert.Equal(0, result.HistoryEntry.SkippedCount);
    Assert.Contains("采纳", record.Targets[0].StatusReason, StringComparison.Ordinal);
    Assert.NotEmpty(record.Targets[0].LastSynchronizedSnapshot);
}
```

- [ ] **Step 2: 运行测试确认当前实现失败**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_should_adopt_existing_directory_mirror_and_persist_snapshots"
```

Expected: FAIL，原因应是 `ExecuteAsync` 仍把已存在目录直接计为跳过。

- [ ] **Step 3: 扩展执行接口与执行分支**

把接口和调用统一调整为：

```csharp
Task<LinkExecutionBatchResult> ExecuteAsync(
    LinkExecutionPlan plan,
    bool allowDowngrade,
    bool allowMirrorAdoption);
```

在 `ExecuteAsync(...)` 中新增目录镜像分支：

```csharp
if (plannedTarget.Mode == ManagedPathMode.DirectoryMirror &&
    plannedTarget.MirrorDisposition == MirrorTargetDisposition.AdoptExisting)
{
    if (!allowMirrorAdoption)
    {
        throw new InvalidOperationException("存在未确认的镜像采纳项，不能直接执行。");
    }

    appliedStrategy = LinkCreationStrategy.Auto;
    historyEntry.SuccessCount++;
    historyEntry.FailureReasons.RemoveAll(_ => false);
}
```

并在 `CreateManagedTargetRecord(...)` 中按 `MirrorDisposition` 区分文案：

```csharp
StatusReason = plannedTarget.MirrorDisposition == MirrorTargetDisposition.AdoptExisting
    ? "已采纳现有镜像并建立受管记录。"
    : "已完成首次镜像拷贝。";
```

- [ ] **Step 4: 增加统一确认窗口并接入预设/批量执行入口**

为 `MirrorAdoptionConfirmationWindow` 建立与降级确认窗口对齐的最小模型：

```csharp
public partial class MirrorAdoptionConfirmationWindow : Window
{
    public MirrorAdoptionConfirmationWindow(IEnumerable<LinkExecutionMirrorAdoptionItem> items)
    {
        InitializeComponent();
        AdoptionItemsGrid.ItemsSource = items.ToList();
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
```

在 `MainWindow.xaml.cs` 的两个入口都按顺序处理：

```csharp
var allowMirrorAdoption = true;
if (plan.MirrorAdoptions.Count > 0)
{
    var adoptionWindow = new MirrorAdoptionConfirmationWindow(plan.MirrorAdoptions)
    {
        Owner = this,
    };
    allowMirrorAdoption = adoptionWindow.ShowDialog() == true;
}
```

- [ ] **Step 5: 更新 `ShellViewModel` 入口签名**

把两个调用改成：

```csharp
public Task<LinkExecutionBatchResult> ApplySelectedPresetAsync(
    LinkExecutionPlan plan,
    bool allowDowngrade,
    bool allowMirrorAdoption)

public Task<LinkExecutionBatchResult> ExecutePendingTasksAsync(
    LinkExecutionPlan plan,
    bool allowDowngrade,
    bool allowMirrorAdoption)
```

内部统一调用：

```csharp
var result = await linkOperationService.ExecuteAsync(plan, allowDowngrade, allowMirrorAdoption);
```

- [ ] **Step 6: 重跑目录镜像采纳测试**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_should_adopt_existing_directory_mirror_and_persist_snapshots"
```

Expected: PASS

- [ ] **Step 7: 为“未确认采纳项不能执行”补一条保护测试**

补充测试：

```csharp
[Fact]
public async Task ExecuteAsync_should_block_when_adoptable_directory_mirror_is_not_confirmed()
{
    // 构造 adoptable plan
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: false));
}
```

- [ ] **Step 8: 运行镜像执行相关测试并提交**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~LinkExecutionServiceTests"
```

Expected: PASS

```bash
git add src/WinLink.App/Services/Interfaces.cs src/WinLink.App/Services/LinkOperationService.cs src/WinLink.App/ViewModels/ShellViewModel.cs src/WinLink.App/MainWindow.xaml.cs src/WinLink.App/Dialogs/MirrorAdoptionConfirmationWindow.xaml src/WinLink.App/Dialogs/MirrorAdoptionConfirmationWindow.xaml.cs tests/WinLink.App.Tests/LinkExecutionServiceTests.cs
git commit -m "feat: adopt existing directory mirrors with confirmation"
```

### Task 3: 统一预设预览与工作台轻校验语义

**Files:**
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Models\PresetTemplateModels.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\PresetTemplateService.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\LinkTaskWorkbenchService.cs`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\MainWindow.xaml`
- Test: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\PresetTemplateServiceTests.cs`
- Test: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\LinkExecutionServiceTests.cs`

- [ ] **Step 1: 为预设预览增加状态枚举**

在 `PresetTemplateModels.cs` 新增：

```csharp
public enum PresetTargetPreviewState
{
    Ready = 0,
    Conflict = 1,
    AdoptableMirror = 2,
}
```

并把 `PresetTemplatePreviewTarget` 调整为：

```csharp
public PresetTargetPreviewState PreviewState { get; set; }
public bool HasConflict => PreviewState == PresetTargetPreviewState.Conflict;
```

- [ ] **Step 2: 先写失败测试，定义“已存在但可采纳”的预览**

在 `PresetTemplateServiceTests.cs` 增加：

```csharp
[Fact]
public void BuildPreview_should_mark_adoptable_directory_mirror_targets()
{
    var tempRoot = CreateTemporaryDirectory();
    Environment.SetEnvironmentVariable("WINLINK_PRESET_HOME", tempRoot);
    var sourcePath = Path.Combine(tempRoot, "skills");
    var targetPath = Path.Combine(tempRoot, "workspace", ".claude", "skills");
    Directory.CreateDirectory(sourcePath);
    File.WriteAllText(Path.Combine(sourcePath, "config.json"), "{ }");
    DirectoryMirrorService.MirrorDirectory(sourcePath, targetPath);

    var preset = new PresetTemplateDefinition
    {
        Name = "skills 目录同步",
        SourcePathTemplate = "%WINLINK_PRESET_HOME%\\skills",
        Targets = [new PresetTemplateTarget { DisplayName = "Claude skills", TargetPathTemplate = targetPath }],
    };

    var service = new PresetTemplateService(new PathEnvironmentService(), tempRoot);
    var preview = service.BuildPreview(preset);

    Assert.Equal(PresetTargetPreviewState.AdoptableMirror, preview.Targets[0].PreviewState);
}
```

- [ ] **Step 3: 运行预览测试确认失败**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~BuildPreview_should_mark_adoptable_directory_mirror_targets"
```

Expected: FAIL，原因应是预览仍只输出布尔冲突。

- [ ] **Step 4: 在 `PresetTemplateService` 中接入目录快照比较**

修改 `BuildPreview(...)`：

```csharp
var sourceKind = pathEnvironmentService.DetectSourceKind(expandedSourcePath);
var previewState = ResolvePreviewState(sourceKind, expandedSourcePath, expandedTargetPath);
```

新增辅助方法：

```csharp
private static PresetTargetPreviewState ResolvePreviewState(
    LinkSourceKind sourceKind,
    string sourcePath,
    string targetPath)
{
    if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
    {
        return PresetTargetPreviewState.Ready;
    }

    if (sourceKind == LinkSourceKind.Directory &&
        Directory.Exists(targetPath) &&
        DirectoryMirrorService.SnapshotsEqual(
            DirectoryMirrorService.CaptureSnapshot(sourcePath),
            DirectoryMirrorService.CaptureSnapshot(targetPath)))
    {
        return PresetTargetPreviewState.AdoptableMirror;
    }

    return PresetTargetPreviewState.Conflict;
}
```

- [ ] **Step 5: 统一工作台轻校验文案**

在 `LinkTaskWorkbenchService.GetLightValidationMessage(...)` 中把目录镜像已存在情况改成：

```csharp
if (task.Mode == ManagedPathMode.DirectoryMirror && Directory.Exists(target.TargetPath))
{
    return DirectoryMirrorService.SnapshotsEqual(
        DirectoryMirrorService.CaptureSnapshot(task.SourcePath),
        DirectoryMirrorService.CaptureSnapshot(target.TargetPath))
        ? "目标目录已存在且与源一致，执行时可登记为受管镜像。"
        : "目标目录已存在且与源不一致，请先处理冲突。";
}
```

- [ ] **Step 6: 调整预设页展示文案**

把 `MainWindow.xaml` 预设表中原来的 `HasConflict` 列替换为状态列，例如：

```xml
<DataGridTextColumn Width="120" Binding="{Binding PreviewState}" Header="预览状态" />
```

并把说明文本改成能覆盖“可采纳已有镜像”的提示。

- [ ] **Step 7: 运行预设与轻校验相关测试并提交**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~PresetTemplateServiceTests|FullyQualifiedName~ValidateAsync"
```

Expected: PASS

```bash
git add src/WinLink.App/Models/PresetTemplateModels.cs src/WinLink.App/Services/PresetTemplateService.cs src/WinLink.App/Services/LinkTaskWorkbenchService.cs src/WinLink.App/MainWindow.xaml tests/WinLink.App.Tests/PresetTemplateServiceTests.cs tests/WinLink.App.Tests/LinkExecutionServiceTests.cs
git commit -m "feat: expose adoptable mirror state in previews"
```

### Task 4: 统一“已连接”页详情展开与移除记录后的选中保持

**Files:**
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\MainWindow.xaml`
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\ViewModels\ShellViewModel.cs`
- Test: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\ManagedLinkRegistryTests.cs`

- [ ] **Step 1: 先写失败测试，定义移除记录后的选中保持**

在 `ManagedLinkRegistryTests.cs` 新增：

```csharp
[Fact]
public async Task RemoveManagedTargetRecordAsync_should_keep_current_record_selected_when_other_targets_remain()
{
    var tempRoot = CreateTemporaryDirectory();
    var directories = new AppDirectories(tempRoot);
    var storage = new JsonRegistryStorageService(directories);
    var record = new ManagedLinkRecord
    {
        Id = "record-a",
        DisplayName = "skills 镜像",
        SourcePath = Path.Combine(tempRoot, "source"),
        SourceKind = LinkSourceKind.Directory,
        Targets =
        [
            new ManagedLinkTargetRecord { Id = "target-1", DisplayName = "A", TargetPath = Path.Combine(tempRoot, "a") },
            new ManagedLinkTargetRecord { Id = "target-2", DisplayName = "B", TargetPath = Path.Combine(tempRoot, "b") },
        ],
    };
    await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument { Records = [record] });

    var viewModel = CreateShellViewModel(tempRoot, storage);
    viewModel.SelectedManagedLink = viewModel.ManagedLinks.Single();
    viewModel.SelectedManagedTarget = viewModel.SelectedManagedLink.Targets.Single(t => t.Id == "target-2");

    await viewModel.RemoveManagedTargetRecordAsync(viewModel.SelectedManagedTarget);

    Assert.Equal("record-a", viewModel.SelectedManagedLink?.Id);
    Assert.Single(viewModel.SelectedManagedLink?.Targets ?? []);
}
```

- [ ] **Step 2: 运行测试确认当前行为失败**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~RemoveManagedTargetRecordAsync_should_keep_current_record_selected_when_other_targets_remain"
```

Expected: FAIL，原因应是刷新后跳回默认第一条或丢失当前目标。

- [ ] **Step 3: 去掉 `RowDetailsVisibilityMode` 和 `RowDetailsTemplate`**

把 `MainWindow.xaml` 中目标表：

```xml
RowDetailsVisibilityMode="VisibleWhenSelected"
```

整段 `DataGrid.RowDetailsTemplate` 删除，并把详细信息并入现有 `Expander`：

```xml
<Expander Header="查看详情">
    <StackPanel Margin="0,8,0,0">
        <TextBlock Text="{Binding StatusReason}" TextWrapping="Wrap" />
        <TextBlock Foreground="#5F6B7A" Text="{Binding AppliedStrategy, StringFormat=最近策略：{0}}" />
        <TextBlock Foreground="#5F6B7A" Text="{Binding LastSynchronizedAt, StringFormat=最近同步：{0:G}}" />
        <TextBlock Foreground="#5F6B7A" Text="{Binding LastCheckedAt, StringFormat=最近检查：{0:G}}" />
    </StackPanel>
</Expander>
```

- [ ] **Step 4: 为移除记录计算“保留当前源 + 合理目标”**

在 `ShellViewModel.RemoveManagedTargetRecordAsync(...)` 中记录：

```csharp
var selectedRecordId = recordContext.Id;
var remainingTargetId = SelectedManagedLink.Targets
    .Where(item => !string.Equals(item.Id, target.Id, StringComparison.Ordinal))
    .Select(item => item.Id)
    .FirstOrDefault();
```

刷新时改为：

```csharp
LoadManagedLinksFromStorage(selectedRecordId, remainingTargetId);
```

如果当前记录被删空，`ApplyManagedLinks(...)` 会自然退回第一条；不再额外覆盖。

- [ ] **Step 5: 补一条“删空后才回退默认项”的测试**

新增：

```csharp
[Fact]
public async Task RemoveManagedTargetRecordAsync_should_fallback_only_when_current_record_is_removed_entirely()
{
    // 当前记录只有一个目标，删除后验证 SelectedManagedLink 回退到另一条记录
}
```

断言：

```csharp
Assert.Equal("record-b", viewModel.SelectedManagedLink?.Id);
```

- [ ] **Step 6: 运行受管列表相关测试并提交**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter "FullyQualifiedName~ManagedLinkRegistryTests"
```

Expected: PASS

```bash
git add src/WinLink.App/MainWindow.xaml src/WinLink.App/ViewModels/ShellViewModel.cs tests/WinLink.App.Tests/ManagedLinkRegistryTests.cs
git commit -m "fix: preserve managed target context after record removal"
```

### Task 5: 全量回归与收尾

**Files:**
- Modify: `E:\project\CSharpProject\winlink\src\WinLink.App\Services\ExecutionResultMessageFormatter.cs`（如历史文案需要）
- Modify: `E:\project\CSharpProject\winlink\tests\WinLink.App.Tests\ExecutionResultMessageFormatterTests.cs`（如格式受影响）
- Review: `E:\project\CSharpProject\winlink\openspec\changes\improve-directory-mirror-adoption-and-managed-target-ux\*.md`

- [ ] **Step 1: 运行受影响测试全集**

Run:

```powershell
dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj
```

Expected: PASS

- [ ] **Step 2: 检查历史结果文案是否仍然准确**

确认以下摘要仍成立：

```csharp
var summary = $"成功 {entry.SuccessCount}，跳过 {entry.SkippedCount}，失败 {entry.FailedCount}";
```

若“采纳已有镜像”需要单独成功说明，仅在 `FailureReasons` 为空时保持现格式，不要把采纳成功误塞进失败原因列表。

- [ ] **Step 3: 手动检查 OpenSpec 产物与实现范围一致**

检查：

```text
openspec/changes/improve-directory-mirror-adoption-and-managed-target-ux/proposal.md
openspec/changes/improve-directory-mirror-adoption-and-managed-target-ux/design.md
openspec/changes/improve-directory-mirror-adoption-and-managed-target-ux/specs/
openspec/changes/improve-directory-mirror-adoption-and-managed-target-ux/tasks.md
openspec/changes/improve-directory-mirror-adoption-and-managed-target-ux/plan.md
```

确认实现没有超出以下 3 组范围：
- 目录镜像采纳
- 已连接页详情交互统一
- 移除记录后的选中保持

- [ ] **Step 4: 提交回归修正**

```bash
git add src/WinLink.App/Services/ExecutionResultMessageFormatter.cs tests/WinLink.App.Tests/ExecutionResultMessageFormatterTests.cs
git commit -m "test: finalize mirror adoption and managed target ux regression coverage"
```
