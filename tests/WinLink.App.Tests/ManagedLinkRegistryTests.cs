using WinLink.App.Infrastructure;
using WinLink.App.Models;
using WinLink.App.Services;
using WinLink.App.ViewModels;

namespace WinLink.App.Tests;

public sealed class ManagedLinkRegistryTests
{
    [Fact]
    public async Task RefreshAsync_should_mark_matching_hard_link_as_active()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");

            var backend = new WindowsLinkBackendService();
            await backend.CreateAsync(LinkSourceKind.File, sourcePath, targetPath, LinkCreationStrategy.HardLink);

            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);
            var service = new LinkStatusService();

            var refreshed = await service.RefreshAsync(record);

            Assert.Equal(LinkTargetState.Active, refreshed.Targets[0].State);
            Assert.Contains("匹配", refreshed.Targets[0].StatusReason, StringComparison.Ordinal);
            Assert.NotNull(refreshed.Targets[0].LastCheckedAt);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshAsync_should_mark_missing_target_as_disconnected()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");

            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);
            var service = new LinkStatusService();

            var refreshed = await service.RefreshAsync(record);

            Assert.Equal(LinkTargetState.Disconnected, refreshed.Targets[0].State);
            Assert.Contains("不存在", refreshed.Targets[0].StatusReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_should_delete_only_the_matching_link_target()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");

            var backend = new WindowsLinkBackendService();
            await backend.CreateAsync(LinkSourceKind.File, sourcePath, targetPath, LinkCreationStrategy.HardLink);

            var service = CreateExecutionService(tempRoot, backend);
            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);

            var reason = await service.DeleteAsync(record, record.Targets[0]);

            Assert.Null(reason);
            Assert.True(File.Exists(sourcePath));
            Assert.False(File.Exists(targetPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_should_block_when_target_no_longer_matches_record()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "replacement");

            var service = CreateExecutionService(tempRoot, new WindowsLinkBackendService());
            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);

            var reason = await service.DeleteAsync(record, record.Targets[0]);

            Assert.NotNull(reason);
            Assert.Contains("不再", reason, StringComparison.Ordinal);
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildAsync_should_recreate_disconnected_target_and_write_history()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");

            var backend = new WindowsLinkBackendService();
            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            var service = CreateExecutionService(tempRoot, backend);
            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [record],
            });

            var reason = await service.RebuildAsync(record, record.Targets[0]);
            var history = await storage.LoadHistoryAsync();

            Assert.Null(reason);
            Assert.True(File.Exists(targetPath));
            Assert.Single(history.Entries);
            Assert.Equal(1, history.Entries[0].SuccessCount);
            Assert.Equal(0, history.Entries[0].FailedCount);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveRecordAsync_should_only_remove_registry_entry_without_touching_disk_entity()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            File.WriteAllText(sourcePath, "content");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "replacement");

            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            var service = CreateExecutionService(tempRoot, new WindowsLinkBackendService());
            var record = CreateRecord(sourcePath, LinkSourceKind.File, targetPath, LinkCreationStrategy.HardLink);
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [record],
            });

            await service.RemoveRecordAsync(record, record.Targets[0]);
            var registry = await storage.LoadRegistryAsync();

            Assert.Empty(registry.Records);
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ShellViewModel_should_restore_managed_link_selection_and_expanded_state_from_ui_state()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var recordA = CreateRecord(Path.Combine(tempRoot, "source-a.txt"), LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            recordA.Id = "record-a";
            var recordB = CreateRecord(Path.Combine(tempRoot, "source-b.txt"), LinkSourceKind.File, Path.Combine(tempRoot, "links", "b.txt"), LinkCreationStrategy.HardLink);
            recordB.Id = "record-b";
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [recordA, recordB],
            });
            await storage.SaveUiStateAsync(new WorkspaceUiStateDocument
            {
                SelectedManagedLinkId = "record-b",
                ExpandedManagedLinkIds = ["record-a"],
            });

            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                directories,
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            Assert.Equal("record-b", viewModel.SelectedManagedLink?.Id);
            Assert.True(viewModel.ManagedLinks.Single(item => item.Id == "record-a").IsExpanded);
            Assert.False(viewModel.ManagedLinks.Single(item => item.Id == "record-b").IsExpanded);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ApplySelectedPresetAsync_should_reuse_standard_validation_and_execution_pipeline()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var pathService = new PathEnvironmentService();
            var workbenchService = new LinkTaskWorkbenchService(pathService);
            var backend = new FakePresetApplyBackendService();
            var sourcePath = Path.Combine(tempRoot, "source", "AGENTS.md");
            var targetPath = Path.Combine(tempRoot, "workspace", "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "content");

            var viewModel = new ShellViewModel(
                directories,
                pathService,
                workbenchService,
                new LinkOperationService(pathService, workbenchService, storage, backend),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);
            var preset = new PresetTemplateDefinition
            {
                Name = "AGENTS direct apply",
                SourcePathTemplate = sourcePath,
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "workspace",
                        TargetPathTemplate = targetPath,
                    },
                ],
            };
            viewModel.Presets.Add(preset);
            viewModel.SelectedPreset = preset;

            var issues = await viewModel.ValidateSelectedPresetAsync();
            var plan = await viewModel.BuildSelectedPresetExecutionPlanAsync();
            var result = await viewModel.ApplySelectedPresetAsync(plan, allowDowngrade: true);
            var registry = await storage.LoadRegistryAsync();
            var history = await storage.LoadHistoryAsync();

            Assert.Empty(issues);
            Assert.True(File.Exists(targetPath));
            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.Single(registry.Records);
            Assert.Single(history.Entries);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ManagedLinkRecord CreateRecord(
        string sourcePath,
        LinkSourceKind sourceKind,
        string targetPath,
        LinkCreationStrategy strategy)
    {
        return new ManagedLinkRecord
        {
            DisplayName = "AGENTS 分发",
            SourcePath = sourcePath,
            SourceKind = sourceKind,
            PreferredStrategy = strategy,
            Targets =
            [
                new ManagedLinkTargetRecord
                {
                    DisplayName = Path.GetFileName(targetPath),
                    TargetPath = targetPath,
                    AppliedStrategy = strategy,
                },
            ],
        };
    }

    private static LinkOperationService CreateExecutionService(string storageRoot, ILinkBackendService backend)
    {
        var pathService = new PathEnvironmentService();
        var workbenchService = new LinkTaskWorkbenchService(pathService);
        var storage = new JsonRegistryStorageService(new AppDirectories(storageRoot));
        return new LinkOperationService(pathService, workbenchService, storage, backend);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinLink.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoopLinkOperationService : ILinkOperationService
    {
        public Task<LinkExecutionPlan> PlanExecutionAsync(IEnumerable<LinkTaskDraft> tasks)
        {
            return Task.FromResult(new LinkExecutionPlan());
        }

        public Task<IReadOnlyList<string>> ValidateAsync(IEnumerable<LinkTaskDraft> tasks)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<LinkExecutionBatchResult> ExecuteAsync(LinkExecutionPlan plan, bool allowDowngrade)
        {
            return Task.FromResult(new LinkExecutionBatchResult());
        }

        public Task<string?> DeleteAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> RebuildAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
        {
            return Task.FromResult<string?>(null);
        }

        public Task RemoveRecordAsync(ManagedLinkRecord record, ManagedLinkTargetRecord target)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLinkStatusService : ILinkStatusService
    {
        public Task<ManagedLinkRecord> RefreshAsync(ManagedLinkRecord record)
        {
            return Task.FromResult(record);
        }
    }

    private sealed class FakePresetApplyBackendService : ILinkBackendService
    {
        public bool Supports(LinkSourceKind sourceKind, LinkCreationStrategy strategy)
        {
            return sourceKind == LinkSourceKind.File && strategy == LinkCreationStrategy.SymbolicLink;
        }

        public Task CreateAsync(LinkSourceKind sourceKind, string sourcePath, string targetPath, LinkCreationStrategy strategy)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, $"linked:{sourcePath}:{strategy}");
            return Task.CompletedTask;
        }
    }
}
