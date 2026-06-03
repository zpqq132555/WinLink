# Directory Mirror Sync Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development
> to implement this plan task-by-task.

**Goal:** 为 WinLink 增加目录镜像同步模式，使目录源能够真实复制到多个目标目录，并在启动或刷新时检测变更、提示同步和处理目标本地改动。

**Architecture:** 在现有链接任务模型之外补充镜像模式字段和同步基线字段，由新的目录快照/镜像同步逻辑负责比较源与目标状态。UI 继续复用 `ShellViewModel` 和受管列表入口，但在新建任务、刷新流程和同步确认弹窗中增加镜像分支。

**Tech Stack:** C# / .NET 8 / WPF / OpenSpec / xUnit

---

## Task 1: 镜像模型与状态基础

- [ ] **Step 1:** 更新 `src/WinLink.App/Models/LinkModels.cs`，为任务、计划和受管记录补充镜像模式、同步基线和镜像状态字段。
- [ ] **Step 2:** 在 `src/WinLink.App/Services/Interfaces.cs` 中补充镜像同步相关接口输入或返回模型，避免后续直接把镜像逻辑塞进现有链接语义。
- [ ] **Step 3:** 在 `tests/WinLink.App.Tests` 新增或更新模型序列化相关测试，确认旧记录默认仍按链接模式读取。
- [ ] **Step 4:** 运行 `dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter Model` 或等价最小测试集，确认模型层改动可编译。

## Task 2: 目录快照与真镜像执行

- [ ] **Step 1:** 新增目录快照帮助逻辑，计算相对路径、大小和最后写入时间摘要。
- [ ] **Step 2:** 在 `src/WinLink.App/Services/LinkOperationService.cs` 中实现镜像模式首次复制与“同步到所有目标”的真镜像分支。
- [ ] **Step 3:** 在执行历史中记录镜像同步成功、跳过和失败结果，保持与现有批量执行摘要一致。
- [ ] **Step 4:** 为源目录新增、修改、删除三类同步行为补充服务测试并运行对应测试集。

## Task 3: 目标本地改动与删除动作

- [ ] **Step 1:** 在状态检查逻辑中识别目标目录自上次同步后的本地改动，并生成可供 UI 使用的状态或原因文案。
- [ ] **Step 2:** 为镜像目标补充“删除目标目录实体”和“只移除记录”两种动作分支。
- [ ] **Step 3:** 为目标被手工改动、删除镜像目标和只移除记录三类行为补充测试。
- [ ] **Step 4:** 运行 `dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj --filter ManagedLinkRegistry` 或等价测试集。

## Task 4: 工作台与同步提示交互

- [ ] **Step 1:** 更新 `src/WinLink.App/ViewModels/ShellViewModel.cs` 和 `src/WinLink.App/MainWindow.xaml`，为目录源增加链接模式/镜像模式选择。
- [ ] **Step 2:** 在应用启动、进入受管页签和手动刷新时接入镜像源检查，并统一决定是否弹出同步提示。
- [ ] **Step 3:** 在同步前对存在本地改动的目标给出覆盖确认，并在状态区显示待同步或警告信息。
- [ ] **Step 4:** 为 ViewModel 和主窗口交互补充测试；若难以自动化，至少补足 ViewModel 层行为测试。

## Task 5: 收尾验证

- [ ] **Step 1:** 通跑 `dotnet test tests/WinLink.App.Tests/WinLink.App.Tests.csproj`。
- [ ] **Step 2:** 运行 `openspec validate --all --json`，检查本 change 的 planning 产物结构是否有效。
- [ ] **Step 3:** 对照 `openspec/changes/add-directory-mirror-sync/specs/directory-mirror-sync/spec.md` 做一次实现前自查，确认没有遗漏“真镜像、启动/刷新提示、目标本地改动确认、删除镜像目标”四个核心点。
