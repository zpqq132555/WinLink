# build-winlink-desktop-link-manager

WinLink 首版 Windows 桌面链接管理工具的实现说明、手工验证清单和需求覆盖记录。

## 开发说明

- 应用入口在 `src/WinLink.App/App.xaml.cs`，启动时会组装 `PathEnvironmentService`、`LinkTaskWorkbenchService`、`LinkStatusService`、`LinkOperationService` 和 `JsonRegistryStorageService`。
- 运行时数据统一落在 `AppData\Local\WinLink`，对应三个 JSON 文件：
  - `registry.json`：受管源任务和目标项
  - `history.json`：最近执行历史
  - `ui-state.json`：`已链接` 页的选中项和展开状态
- `LinkOperationService` 负责三类核心行为：
  - 工作台完整校验和批量执行
  - 严格删除、重建、移除记录
  - 执行结果写回注册表和历史
- `LinkStatusService` 负责把文件系统实际状态映射为 `生效中 / 失效 / 已断开`，并生成可读原因。
- `ShellViewModel` 负责三块 UI 工作区：
  - `新建链接`：任务编辑、校验、批量执行
  - `已链接`：状态刷新、严格删除、重建、移除记录
  - `预设`：预览、直接应用、转成普通任务

## 手工验证清单

1. 新建普通任务
   - 选择一个真实文件源和两个不同目标路径。
   - 先点 `先校验全部`，确认没有错误。
   - 点 `批量执行`，确认弹出执行结果摘要。
   - 切到 `已链接`，确认摘要、左侧列表和右侧目标项都已更新。

2. 应用预设
   - 在 `预设` 页选择一个预设，先检查真实路径和冲突提示。
   - 点击 `直接应用`，确认它会复用同样的校验和降级确认流程。
   - 执行完成后检查 `已链接` 和 `执行历史` 是否同步出现新记录。

3. 刷新状态
   - 在 `已链接` 页首次进入时，确认状态会自动刷新一次。
   - 手动修改某个目标，使其缺失、替换或改指向。
   - 点击菜单 `刷新已链接状态`，确认状态会变成 `失效` 或 `已断开`，并能查看详细原因。

4. 重建链接
   - 删除一个受管目标链接对象。
   - 在 `已链接` 页选中它并点 `重建链接`。
   - 确认目标被重新创建，状态恢复为 `生效中`，并在 `执行历史` 中留下新记录。

5. 严格删除和移除记录
   - 对仍与记录匹配的目标执行 `删除链接`，确认只删除目标实体，源文件保持不变。
   - 对已经被替换或改指向的目标执行 `删除链接`，确认操作被阻止并给出后续建议。
   - 对任意目标执行 `移除记录`，确认注册表项消失，但磁盘上的当前实体保持不变。

## 需求覆盖检查

- `desktop-workspace-shell`
  - 已覆盖主窗口、三标签页、菜单栏和辅助弹窗入口。
- `link-task-workbench`
  - 已覆盖任务编辑、轻量预检查、完整校验、推荐策略、降级确认和尽力执行。
- `managed-link-registry`
  - 已覆盖注册表写入、状态刷新、状态原因、严格删除、重建、移除记录和展开状态恢复。
- `preset-link-templates`
  - 已覆盖固定预设、变量路径预览、冲突提示、直接应用和转成普通任务。
- `execution-history`
  - 已覆盖执行摘要、轻量历史列表以及历史弹窗明细查看。

## 验证命令

```powershell
dotnet test tests\WinLink.App.Tests\WinLink.App.Tests.csproj
dotnet build WinLink.slnx
```
