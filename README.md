# WinLink

WinLink 是一个面向 Windows 的桌面链接管理工具，用来替代直接手写 `mklink` 命令的工作流。它把“创建链接、校验冲突、记录受管关系、后续重建或安全删除”收拢到一个 WPF 桌面界面里，避免命令参数易错、状态难追踪和误删真实文件的问题。

## 当前基线能力

- `新建链接`：支持一个源映射多个目标，提供编辑期预检查、执行前完整校验、推荐策略与降级确认。
- `已链接`：只展示 WinLink 自己记录过的链接，支持状态刷新、原因查看、重建链接、删除链接和移除记录。
- `预设`：内置 AGENTS 主配置分发与 skills 目录同步两个预设，可直接执行，也可转成普通任务继续编辑。
- `执行历史`：保留最近执行摘要与失败原因，便于回看。
- `本地持久化`：运行时数据写入 `%LOCALAPPDATA%\\WinLink` 下的 `registry.json`、`history.json` 和 `ui-state.json`。

## 运行要求

- Windows 10 / 11
- .NET 8 SDK
- 建议开启 Windows Developer Mode；如果符号链接权限不可用，WinLink 会按能力提示降级为 `HardLink` 或 `Junction`

## 本地运行

在仓库根目录执行：

```powershell
dotnet build WinLink.slnx
dotnet run --project src\WinLink.App\WinLink.App.csproj
```

## 测试

```powershell
dotnet test tests\WinLink.App.Tests\WinLink.App.Tests.csproj
```

## 项目结构

- `src/WinLink.App`：WPF 桌面应用本体
- `tests/WinLink.App.Tests`：核心服务与回归测试
- `openspec/specs`：当前生效的主规格
- `openspec/changes/archive/2026-05-25-build-winlink-desktop-link-manager`：首版桌面链接管理器的归档变更材料

## 需求追溯

首个桌面工具基线对应的 OpenSpec 变更已经归档在 `openspec/changes/archive/2026-05-25-build-winlink-desktop-link-manager`，其中保留了：

- `proposal.md`：为什么要做这个工具、范围边界是什么
- `design.md`：首版桌面产品、存储模型和交互约束
- `tasks.md`：实现拆解与完成勾选记录
- `README.md`：当时的实现说明、手工验证清单和需求覆盖检查
- `specs/*/spec.md`：该变更引入的原始 delta specs

归档后，规范入口仍然在 `openspec/specs`：

- `desktop-workspace-shell`
- `execution-history`
- `link-task-workbench`
- `managed-link-registry`
- `preset-link-templates`

其中 `desktop-workspace-shell`、`execution-history` 和 `preset-link-templates` 的主规格与首个归档 change 保持同一需求语义；`link-task-workbench` 与 `managed-link-registry` 在保留首版要求的基础上，继续吸收了后续 `support-reusing-managed-link-sources` 变更，因此现在的主规格是“首版基线 + 后续增强”的规范并集，不会因为归档首个 change 而丢失需求痕迹。
