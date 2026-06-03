# Retrospective: add-directory-mirror-sync

> Written: 2026-06-03 (after verify passed)  
> Commit range: `3582d164e3ada2d646dab35c67144c8993e999a1..4f406d1`  
> Worktree: `E:\project\CSharpProject\winlink`

---

## 0. Evidence

- **Commit range**: `3582d164e3ada2d646dab35c67144c8993e999a1..4f406d1` (1 commits)
- **Diff size**: `+1536 / -198 lines across 21 files`
- **Tasks done**: `9/9`
- **Active hours**: 约 3 小时
- **Subagent dispatches**: n/a
- **New external dependencies**: none
- **Bugs encountered post-merge**: none
- **OpenSpec validate state at archive**: pass
- **Test coverage signal**: `dotnet test` 通过 46/46

Commit chain（时序）:

```text
3582d164e3ada2d646dab35c67144c8993e999a1 <base>
4f406d1 添加目录镜像同步功能
```

---

## 1. Wins

- [evidence: `src/WinLink.App/Services/LinkOperationService.cs`, `src/WinLink.App/Services/DirectoryMirrorService.cs`] 把“目录镜像”从现有链接策略中独立出来，避免了把真实拷贝语义硬塞进 `LinkCreationStrategy`。
- [evidence: `src/WinLink.App/Services/LinkStatusService.cs`, `src/WinLink.App/ViewModels/ShellViewModel.cs`, `src/WinLink.App/MainWindow.xaml.cs`] 启动、刷新、进入“已连接”页三条入口统一接入镜像变更检测与同步提示，用户路径比较完整。
- [evidence: `tests/WinLink.App.Tests/LinkExecutionServiceTests.cs`, `tests/WinLink.App.Tests/ManagedLinkRegistryTests.cs`] 首次镜像、真镜像同步、目标本地改动警告、删除镜像目标等关键行为都有自动化回归。

## 2. Misses

- 🟡 [painful | evidence: `verify.md` §5] 这套 schema 的 `verify` 依赖“先有本地 commit”，和“全部归档完再提交”的直觉流程有冲突，导致归档前需要额外解释一次。
- 🟢 [nit | evidence: `verify.md` §6] 仓库里已有 `docs/superpowers/specs/2026-05-25-winlink-icon-publish-design.md`，会持续触发 front-door routing leak warning，增加 verify 噪音。
- 🟢 [nit | evidence: `src/WinLink.App/MainWindow.xaml`, `MainWindow.xaml.cs`] 为了稳定落地界面改动，直接重写了主窗口 XAML/XAML.cs；虽然结果可控，但说明现有文件的局部可维护性一般。

## 3. Plan deviations

| Plan task | What changed | Why |
|-----------|--------------|-----|
| Task 4.4 | 没有补专门的 UI 自动化测试，主要补足了服务层与 ViewModel 相关回归 | 当前仓库测试基础更偏 xUnit 单元/集成，先确保行为链路可回归 |
| Task 5.3 | 自查与 verify 合并完成，而不是单独再写一轮实现自查说明 | 避免重复记录同一批证据 |

## 4. Skill / workflow compliance

| Skill                                            | Used |
|--------------------------------------------------|------|
| superpowers:brainstorming                        | ✓ |
| superpowers:writing-plans                        | ✓ |
| superpowers:using-git-worktrees                  | ✗ |
| superpowers:subagent-driven-development          | ✗ |
| (transitive) superpowers:test-driven-development | ✗ |
| (transitive) superpowers:requesting-code-review  | ✗ |
| superpowers:finishing-a-development-branch       | ✗ |

### Deliberately Skipped Skills

- **`superpowers:using-git-worktrees`**
  - **What was skipped**: 没有为本次 change 单独建立 worktree。
  - **Why this cycle**: 本次改动在单仓库单线程内完成，且用户与代理共享同一工作区，局部隔离收益不高。
  - **How to prevent recurrence**: `scope-judgment rule`：当 change 涉及并行多人协作、长周期实现或需要隔离实验分支时，默认启用独立 worktree。

- **`superpowers:subagent-driven-development`**
  - **What was skipped**: apply 阶段没有拆给子代理逐 task 执行。
  - **Why this cycle**: 任务虽然跨层，但上下文强耦合，主代理连续实现和回归更高效。
  - **How to prevent recurrence**: `scope-judgment rule`：当任务可以天然切成服务层 / UI 层 / 测试层并行子问题时，再启用 subagent。

- **`superpowers:test-driven-development`**
  - **What was skipped**: 没有严格按“先红后绿”顺序推进所有任务。
  - **Why this cycle**: 这次先完成模型与 UI 链路改造，再补镜像行为回归，节奏上更接近“实现后补足关键测试”。
  - **How to prevent recurrence**: `CLAUDE.md trigger`：对新 capability 的核心行为（尤其是状态机和同步语义）优先先写失败测试，再补实现。

- **`superpowers:requesting-code-review`**
  - **What was skipped**: 没有单独触发代码审查 skill。
  - **Why this cycle**: 当前在本地闭环实现、测试、verify 与 retrospective，未额外引入独立审查回合。
  - **How to prevent recurrence**: `skill description tightening`：在 apply 结束、verify 前加一条显式提示，优先跑一次 review skill。

- **`superpowers:finishing-a-development-branch`**
  - **What was skipped**: 还未进入最终推送 / PR 收尾。
  - **Why this cycle**: 当前仍处于 archive 前后收尾阶段，尚未推进到对外发布分支动作。
  - **How to prevent recurrence**: `one-off — schema boundary case, no prevention possible`：该 skill 本来就属于 archive 之后的后置动作，本阶段不使用是合理边界。

## 5. Surprises

- 原本以为 archive 前可以直接生成 `verify.md`，实际 schema 明确要求先有本地 commit 作为“可审查实现”的证据锚点。

## 6. Promote candidates → long-term learning

- [ ] 🟡 **在 `superpowers-bridge` 流程里，把“verify 需要本地 commit”提前告诉用户** → **Promote to project CLAUDE.md**（流程说明段）
  > **Why**: 这次在 archive 前才暴露这个前置条件，用户对“先 commit 还是先 archive”产生了合理困惑。
  > **How to apply**: 当仓库使用 `superpowers-bridge` 且准备进入 verify/archive 时，先说明“本地 commit 是 verify 前置条件，push 可以等 archive 后再做”。

- [ ] 🟢 **定期清理 `docs/superpowers/specs/` 历史残留，降低 verify 噪音** → **Promote to memory**（type: feedback）
  > **Why**: front-door routing leak warning 是非阻塞项，但会反复占用注意力。
  > **How to apply**: 每次 archive 前，如果 verify 命中历史 leak，顺手核对是否已经被对应 change 捕获并清理。
