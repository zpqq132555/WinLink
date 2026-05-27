# WinLink

WinLink 是一个面向 Windows 的桌面链接管理工具，用来替代直接手写 `mklink` 命令的工作流。它把“创建链接、检查冲突、批量执行、记录受管关系、后续重建或安全删除”放进一个可视化界面里，适合希望稳定维护文件链接和目录链接的用户。

## 这个工具是干嘛的

WinLink 主要解决两类问题：

- 创建链接时容易出错。直接使用 `mklink` 时，参数、源路径、目标路径和链接类型都很容易写错。
- 创建完以后难以维护。时间一长，往往分不清哪些链接是自己建的、哪些已经失效、哪些可以安全删除。

WinLink 的目标不是替代 Windows 文件系统，而是帮你把“创建和维护链接”这件事变成一条更安全、更容易回看的流程。

## 适合什么场景

- 你需要把一个源目录或文件映射到多个目标位置。
- 你经常同步配置、脚本目录、素材目录，想减少重复复制。
- 你希望在执行前先看到冲突和风险，而不是命令跑完才发现问题。
- 你希望后续还能看到这些链接的状态，并在失效后重建。

## 不适合什么场景

- 你只偶尔临时创建一个链接，而且已经很熟悉 `mklink`。
- 你需要扫描并接管系统里所有历史链接。这个能力当前还没有开放。
- 你想要安装包、自动升级或跨平台支持。当前版本聚焦在 Windows 桌面下的链接管理。

## 怎么使用

WinLink 当前主要有三个工作区：`新建链接`、`已链接`、`预设`。

### 最常见的使用流程

1. 打开 `新建链接`
2. 新增一个任务，填写备注名
3. 选择源路径，可以是文件，也可以是目录
4. 添加一个或多个目标路径
5. 先看行内的预检查提示，再点 `先校验全部`
6. 确认没有问题后，点 `批量执行`
7. 后续到 `已链接` 里查看状态、重建链接、删除链接或移除记录

### 三个工作区分别做什么

#### 新建链接

这里负责创建链接任务。

- 支持一个源映射多个目标
- 支持浏览文件、浏览目录和自动识别源类型
- 支持编辑期预检查，提前发现路径冲突和格式问题
- 支持完整校验，执行前再检查源是否存在、目标是否冲突、路径是否完整
- 支持批量执行，适合一次处理多条链接

#### 已链接

这里负责维护 WinLink 自己记录过的链接。

- 只展示 WinLink 管理过的链接，不会把系统里所有链接都扫进来
- 可以刷新状态，查看哪些链接仍然有效、哪些已经断开或异常
- 可以对单个目标执行 `重建链接`、`删除链接`、`移除记录`

`删除链接` 会尝试删除当前链接本身。  
`移除记录` 只从 WinLink 的登记信息里移除，不等于删除真实文件。

#### 预设

这里放的是可以直接复用的内置模板。

- 当前内置了 `AGENTS 主配置分发` 和 `skills 目录同步` 两个预设
- 你可以先看预览，再直接应用
- 也可以把预设转换成普通任务，再继续手工编辑

## 这个工具相比直接用 mklink 的好处

- 不用记命令参数，降低误操作概率
- 可以先检查，再执行，而不是直接修改文件系统
- 可以批量处理多个目标
- 能记录哪些链接是 WinLink 自己创建的
- 后续可以刷新状态、重建、删除或移除记录
- 有执行历史，便于回看每次操作的结果

## 数据会保存在哪里

WinLink 会把运行时数据写到：

```text
%LOCALAPPDATA%\WinLink
```

当前会保存这些文件：

- `registry.json`：受管链接登记信息
- `history.json`：执行历史
- `ui-state.json`：界面状态

## 如何获取和运行

### 直接使用便携版

可以从 GitHub Releases 下载 ZIP，解压后直接运行 `WinLink.App.exe`。

如果便携版在你的机器上无法启动，先检查：

- 是否是 Windows 10 / 11
- 是否已安装 .NET 8 对应的 Windows 桌面运行环境
- 是否开启了 Windows Developer Mode

如果符号链接权限不可用，WinLink 会按能力提示降级为 `HardLink` 或 `Junction`。

### 从源码运行

要求：

- Windows 10 / 11
- .NET 8 SDK

在仓库根目录执行：

```powershell
dotnet build WinLink.slnx
dotnet run --project src\WinLink.App\WinLink.App.csproj
```

## 测试

```powershell
dotnet test tests\WinLink.App.Tests\WinLink.App.Tests.csproj
```

## 发布

仓库根目录提供了 `release.ps1`，用于生成 Windows 便携版发布 ZIP，并按需创建 Git tag / GitHub Release。

### 仅生成本地发布产物

```powershell
.\release.ps1 -Version 1.0.0
```

脚本会执行测试、发布到 `artifacts\publish\<version>\win-x64\`、生成 ZIP，并输出对应的 `SHA256`。

### 创建正式版本 tag 并推送

```powershell
.\release.ps1 -Version 1.0.0 -CreateTag -PushTag
```

要求：

- 工作区必须干净
- 本地和远端不能已存在同名 tag

### 直接创建 GitHub Release

如果本机已安装并登录 GitHub CLI `gh`，可以继续执行：

```powershell
.\release.ps1 -Version 1.0.0 -CreateTag -PushTag -CreateRelease
```

### 手动发布到 GitHub Releases

如果本机没有 `gh`，可以先运行本地打包命令，再去 GitHub 仓库的 Releases 页面手动创建 Release：

1. 选择已有 tag，例如 `v1.0.0`
2. 上传脚本生成的 ZIP 文件，例如 `artifacts\WinLink-1.0.0-portable-win-x64.zip`
3. 将脚本输出的 `SHA256` 写入 Release 说明，便于后续更新 Scoop manifest

当前 ZIP 命名格式为：

```text
WinLink-<version>-portable-win-x64.zip
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
