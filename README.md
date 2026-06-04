# WinLink

WinLink 是一个面向 Windows 的桌面链接管理工具，用来替代直接手写 `mklink` 命令的工作流。它把“创建链接、检查冲突、批量执行、记录受管关系、后续重建、目录镜像同步或安全删除”放进一个可视化界面里，适合希望稳定维护文件链接、目录链接和目录镜像的用户。

## 这个工具是干嘛的

WinLink 主要解决两类问题：

- 创建链接时容易出错。直接使用 `mklink` 时，参数、源路径、目标路径和链接类型都很容易写错。
- 创建完以后难以维护。时间一长，往往分不清哪些链接是自己建的、哪些已经失效、哪些可以安全删除。

WinLink 的目标不是替代 Windows 文件系统，而是帮你把“创建和维护链接”这件事变成一条更安全、更容易回看的流程。

## 适合什么场景

- 你需要把一个源目录或文件映射到多个目标位置。
- 你经常同步配置、脚本目录、素材目录，想减少重复复制。
- 你希望把目录按“真镜像”方式同步到多个目标，并在后续继续维护同步基线。
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
4. 如果源是目录，按需要选择普通链接或 `目录镜像` 管理模式
5. 添加一个或多个目标路径；如果是已有受管源，也可以直接 `复用已链接源`
6. 先看行内的预检查提示，再点 `先校验全部`
7. 确认没有问题后，点 `批量执行`
8. 后续到 `已链接` 里查看状态、重建链接、同步目录镜像、删除目标或移除记录

### 三个工作区分别做什么

#### 新建链接

这里负责创建链接任务。

- 支持一个源映射多个目标
- 支持浏览文件、浏览目录和自动识别源类型
- 支持目录源切换到 `目录镜像` 管理模式；首次执行会真实复制目录，后续可 `同步到所有目标`
- 支持在目标目录已存在且内容与源一致时采纳已有镜像，只登记受管记录和同步基线，不重复拷贝
- 支持 `复用已链接源`，直接给已有受管源继续新增目标
- 支持编辑期预检查，提前发现路径冲突和格式问题
- 支持完整校验，执行前再检查源是否存在、目标是否冲突、路径是否完整
- 支持批量执行，适合一次处理多条链接

#### 已链接

这里负责维护 WinLink 自己记录过的链接和目录镜像。

- 只展示 WinLink 管理过的链接，不会把系统里所有链接都扫进来
- 可以刷新状态，查看哪些链接仍然有效、哪些已经断开或异常
- 目录镜像源发生变化时，会提示你是否 `同步到所有目标`；如果目标存在本地改动，也会先要求确认
- 可以对单个目标执行 `重建链接`、`删除目标`、`移除记录`

`删除目标` 会尝试删除当前受管目标本身；普通链接删除的是链接实体，目录镜像删除的是目标目录实体。  
`移除记录` 只从 WinLink 的登记信息里移除，不等于删除真实文件。

#### 预设

这里放的是可以直接复用的内置模板。

- 当前内置了 `AGENTS 主配置分发` 和 `skills 目录同步` 两个预设
- 预览会提前提示目标冲突、父目录缺失，以及目录镜像目标是否可直接采纳
- 你可以先看预览，再直接应用
- 也可以把预设转换成普通任务，再继续手工编辑

## 这个工具相比直接用 mklink 的好处

- 不用记命令参数，降低误操作概率
- 可以先检查，再执行，而不是直接修改文件系统
- 目录场景既可以创建链接，也可以按真镜像语义持续同步
- 可以批量处理多个目标
- 能记录哪些链接是 WinLink 自己创建的
- 对已经存在且内容一致的镜像目标，可以直接采纳接管，不必重复拷贝
- 后续可以刷新状态、重建、删除或移除记录
- 有执行历史，便于回看每次操作的结果

## 数据会保存在哪里

WinLink 会把运行时数据写到：

```text
%LOCALAPPDATA%\WinLink
```

当前会保存这些文件：

- `registry.json`：受管链接与目录镜像登记信息
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
- `openspec/changes/archive/*`：已归档的需求变更、设计与验收材料

## 需求追溯

当前实现相关的 OpenSpec 归档变更主要包括：

- `2026-05-25-build-winlink-desktop-link-manager`：首版桌面链接管理器基线
- `2026-05-25-support-reusing-managed-link-sources`：支持复用已链接源并继续追加目标
- `2026-06-03-add-directory-mirror-sync`：引入目录镜像同步能力
- `2026-06-04-improve-directory-mirror-adoption-and-managed-target-ux`：补充已有镜像采纳与受管目标交互优化

每个归档 change 目录下都保留了 `proposal.md`、`design.md`、`tasks.md`、`README.md` 与对应的 `specs/*/spec.md`，可用于回看当时的设计取舍、实现拆解和验收记录。

归档后，规范入口仍然在 `openspec/specs`：

- `desktop-workspace-shell`
- `directory-mirror-sync`
- `execution-history`
- `link-task-workbench`
- `managed-link-registry`
- `preset-link-templates`

这些主规格共同构成当前实现的规范入口。它们以首版桌面工具基线为起点，继续吸收了“复用已链接源”“目录镜像同步”“已有镜像采纳与受管目标交互优化”等后续增强，因此现在的主规格是“首版基线 + 后续增强”的规范并集，不会因为单个 change 已归档而丢失需求痕迹。
