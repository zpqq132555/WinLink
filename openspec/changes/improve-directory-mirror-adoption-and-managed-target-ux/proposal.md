## Why

当前目录镜像在 `opsx-init` 等已先完成真实复制的场景下，会把“目标已存在”一律视为冲突或跳过，导致镜像结果无法被登记成受管记录，后续也不能继续沿用同步基线。同时，“已连接”页把选中状态和详情展开混在一起，且移除记录后会丢失当前上下文，造成明显的操作割裂感。现在处理这组问题，可以把目录镜像补成真正可持续维护的闭环，并让受管目标列表的交互更稳定、更符合直觉。

## What Changes

**目录镜像初始化登记**
- From: 目录镜像新建执行时，只要目标已存在，就按冲突/跳过处理。
- To: 当目标目录已存在且与源目录快照一致时，系统提示用户是否“仅登记受管记录”；确认后只写入受管记录与同步基线，不再重拷贝。
- Reason: 支持 `opsx-init` 等先复制、后登记的真实使用路径。
- Impact: 非 breaking；影响目录镜像预览、校验、执行与历史记录语义。

**预设与工作台的镜像冲突提示**
- From: 预设预览和工作台轻校验只区分“已存在/未存在”，已存在统一视为冲突。
- To: 目录镜像目标已存在时，区分“可采纳的已有镜像”和“真正冲突”，把前者作为可确认采纳的状态展示。
- Reason: 让用户在执行前就理解系统将采纳已有镜像，而不是再次创建镜像。
- Impact: 非 breaking；影响预设预览与新建任务编辑期提示。

**已连接页目标详情交互**
- From: 目标表选中某行时会自动展开大块明细，同时“查看详情”只展开部分文本，形成双通道交互。
- To: 目标详情统一收敛到“查看详情”中，由用户主动展开或折叠；选中行只表示当前操作对象，不再自动展开大块详情。
- Reason: 消除“选中即展开”的意外感，使选中与查看详情职责分离。
- Impact: 非 breaking；影响已连接页目标列表展示方式。

**移除记录后的选中保持**
- From: 移除某个目标记录后，页面刷新经常回退到第一条受管源任务。
- To: 删除后优先保留当前受管源任务，并将焦点切到剩余的相邻目标；只有整条受管源任务被删空时，才回退到默认第一条。
- Reason: 保持连续操作上下文，避免列表刷新打断用户。
- Impact: 非 breaking；影响已连接页刷新后的选中恢复逻辑。

## Capabilities

### New Capabilities
- 无

### Modified Capabilities
- `directory-mirror-sync`: 目录镜像首次执行需要支持“已存在且一致时仅登记受管记录”的采纳流程。
- `managed-link-registry`: 已连接页目标详情展示方式与移除记录后的选中保持规则需要调整。
- `preset-link-templates`: 预设预览中的目录镜像目标已存在时，需要区分可采纳状态与真正冲突。

## Impact

- 影响代码：
  - `src/WinLink.App/Services/LinkOperationService.cs`
  - `src/WinLink.App/Services/PresetTemplateService.cs`
  - `src/WinLink.App/Services/LinkTaskWorkbenchService.cs`
  - `src/WinLink.App/ViewModels/ShellViewModel.cs`
  - `src/WinLink.App/MainWindow.xaml`
- 影响的系统行为：
  - 目录镜像的预览、校验、执行、历史记录
  - 已连接页目标详情展示与刷新后的选中恢复
- 影响测试：
  - `tests/WinLink.App.Tests/LinkExecutionServiceTests.cs`
  - `tests/WinLink.App.Tests/ManagedLinkRegistryTests.cs`
