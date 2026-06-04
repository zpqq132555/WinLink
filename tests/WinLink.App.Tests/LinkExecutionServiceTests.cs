using WinLink.App.Infrastructure;
using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.Tests;

public sealed class LinkExecutionServiceTests
{
    [Fact]
    public async Task PlanExecutionAsync_should_collect_downgrade_targets_when_recommended_strategy_is_unavailable()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");

            var task = new LinkTaskDraft
            {
                DisplayName = "AGENTS 分发",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.File,
            };
            var target = new LinkTargetDraft();
            target.ApplyFullPath(targetPath);
            task.Targets.Add(target);

            var backend = new FakeLinkBackendService
            {
                SupportedStrategies =
                {
                    [LinkSourceKind.File] = [LinkCreationStrategy.HardLink],
                },
            };
            var service = CreateExecutionService(tempRoot, backend);

            var plan = await service.PlanExecutionAsync([task]);

            Assert.Single(plan.Downgrades);
            Assert.Equal(LinkCreationStrategy.SymbolicLink, plan.Downgrades[0].RecommendedStrategy);
            Assert.Equal(LinkCreationStrategy.HardLink, plan.Downgrades[0].FallbackStrategy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PlanExecutionAsync_should_collect_adoptable_existing_directory_mirrors()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
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
            Assert.Empty(plan.Downgrades);
            Assert.Equal(MirrorTargetDisposition.AdoptExisting, plan.Targets[0].MirrorDisposition);
            Assert.Equal(targetPath, plan.MirrorAdoptions[0].TargetPath);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_best_effort_create_links_and_persist_registry_and_history()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            var okTargetPath = Path.Combine(tempRoot, "links", "skills");
            var conflictTargetPath = Path.Combine(tempRoot, "occupied", "skills");
            Directory.CreateDirectory(Path.GetDirectoryName(conflictTargetPath)!);
            File.WriteAllText(conflictTargetPath, "existing");

            var task = new LinkTaskDraft
            {
                DisplayName = "skills 同步",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.Directory,
            };
            var okTarget = new LinkTargetDraft();
            okTarget.ApplyFullPath(okTargetPath);
            task.Targets.Add(okTarget);
            var conflictTarget = new LinkTargetDraft();
            conflictTarget.ApplyFullPath(conflictTargetPath);
            task.Targets.Add(conflictTarget);

            var backend = new FakeLinkBackendService
            {
                SupportedStrategies =
                {
                    [LinkSourceKind.Directory] = [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.Junction],
                },
            };
            var service = CreateExecutionService(tempRoot, backend);

            var plan = await service.PlanExecutionAsync([task]);
            var result = await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);
            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            var registry = await storage.LoadRegistryAsync();
            var history = await storage.LoadHistoryAsync();

            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.Equal(1, result.HistoryEntry.SkippedCount);
            Assert.Equal(0, result.HistoryEntry.FailedCount);
            Assert.Single(registry.Records);
            Assert.Single(registry.Records[0].Targets);
            Assert.Equal(okTargetPath, registry.Records[0].Targets[0].TargetPath);
            Assert.Single(history.Entries);
            Assert.Equal("skills 同步", history.Entries[0].Summary);
            Assert.Contains("已存在", history.Entries[0].FailureReasons[0]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_continue_after_backend_failure_and_record_reason()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var firstTargetPath = Path.Combine(tempRoot, "links", "AGENTS.md");
            var secondTargetPath = Path.Combine(tempRoot, "links", "AGENTS.backup.md");

            var task = new LinkTaskDraft
            {
                DisplayName = "AGENTS 分发",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.File,
            };
            var firstTarget = new LinkTargetDraft();
            firstTarget.ApplyFullPath(firstTargetPath);
            task.Targets.Add(firstTarget);
            var secondTarget = new LinkTargetDraft();
            secondTarget.ApplyFullPath(secondTargetPath);
            task.Targets.Add(secondTarget);

            var backend = new FakeLinkBackendService
            {
                SupportedStrategies =
                {
                    [LinkSourceKind.File] = [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.HardLink],
                },
                Failures =
                {
                    [firstTargetPath] = "simulated failure",
                },
            };
            var service = CreateExecutionService(tempRoot, backend);

            var plan = await service.PlanExecutionAsync([task]);
            var result = await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);

            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.Equal(0, result.HistoryEntry.SkippedCount);
            Assert.Equal(1, result.HistoryEntry.FailedCount);
            Assert.Contains("simulated failure", result.HistoryEntry.FailureReasons[0]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_fallback_to_hard_link_when_symbolic_link_lacks_privilege()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var targetPath = Path.Combine(tempRoot, "links", "AGENTS.md");

            var task = new LinkTaskDraft
            {
                DisplayName = "AGENTS 主配置分发",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.File,
            };
            var target = new LinkTargetDraft();
            target.ApplyFullPath(targetPath);
            task.Targets.Add(target);

            var backend = new FakeLinkBackendService
            {
                SupportedStrategies =
                {
                    [LinkSourceKind.File] = [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.HardLink],
                },
                StrategyFailures =
                {
                    [(targetPath, LinkCreationStrategy.SymbolicLink)] = new UnauthorizedAccessException("客户端没有所需的特权。"),
                },
            };
            var service = CreateExecutionService(tempRoot, backend);

            var plan = await service.PlanExecutionAsync([task]);
            var result = await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);
            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            var registry = await storage.LoadRegistryAsync();

            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.Equal(0, result.HistoryEntry.FailedCount);
            Assert.Empty(result.HistoryEntry.FailureReasons);
            Assert.Equal(
                [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.HardLink],
                backend.AttemptedStrategies[targetPath]);
            Assert.Equal(LinkCreationStrategy.HardLink, registry.Records[0].Targets[0].AppliedStrategy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_fallback_to_junction_when_directory_symbolic_link_lacks_privilege()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            var targetPath = Path.Combine(tempRoot, "links", "skills");

            var task = new LinkTaskDraft
            {
                DisplayName = "skills 目录同步",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.Directory,
            };
            var target = new LinkTargetDraft();
            target.ApplyFullPath(targetPath);
            task.Targets.Add(target);

            var backend = new FakeLinkBackendService
            {
                SupportedStrategies =
                {
                    [LinkSourceKind.Directory] = [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.Junction],
                },
                StrategyFailures =
                {
                    [(targetPath, LinkCreationStrategy.SymbolicLink)] = new UnauthorizedAccessException("客户端没有所需的特权。"),
                },
            };
            var service = CreateExecutionService(tempRoot, backend);

            var plan = await service.PlanExecutionAsync([task]);
            var result = await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);
            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            var registry = await storage.LoadRegistryAsync();

            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.Equal(0, result.HistoryEntry.FailedCount);
            Assert.Empty(result.HistoryEntry.FailureReasons);
            Assert.Equal(
                [LinkCreationStrategy.SymbolicLink, LinkCreationStrategy.Junction],
                backend.AttemptedStrategies[targetPath]);
            Assert.Equal(LinkCreationStrategy.Junction, registry.Records[0].Targets[0].AppliedStrategy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_create_directory_mirror_and_persist_snapshots()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "config.json"), "{ }");
            var targetPath = Path.Combine(tempRoot, "mirror", "skills");

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
            var registry = await storage.LoadRegistryAsync();

            Assert.Empty(plan.Downgrades);
            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.True(File.Exists(Path.Combine(targetPath, "config.json")));

            var record = Assert.Single(registry.Records);
            Assert.Equal(ManagedPathMode.DirectoryMirror, record.Mode);
            Assert.NotEmpty(record.LastSynchronizedSourceSnapshot);
            Assert.NotNull(record.LastSynchronizedAt);
            Assert.NotEmpty(record.Targets[0].LastSynchronizedSnapshot);
            Assert.NotNull(record.Targets[0].LastSynchronizedAt);
            Assert.Equal(LinkCreationStrategy.Auto, record.Targets[0].AppliedStrategy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_adopt_existing_directory_mirror_and_persist_snapshots()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
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
            Assert.False(string.IsNullOrWhiteSpace(record.Targets[0].StatusReason));
            Assert.NotEmpty(record.Targets[0].LastSynchronizedSnapshot);
            Assert.Equal(MirrorTargetDisposition.AdoptExisting, plan.Targets[0].MirrorDisposition);
            Assert.Equal(LinkCreationStrategy.Auto, record.Targets[0].AppliedStrategy);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_block_when_adoptable_directory_mirror_is_not_confirmed()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
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

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: false));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SyncMirrorAsync_should_overwrite_removed_and_added_entries_using_true_mirror_semantics()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            var oldFilePath = Path.Combine(sourcePath, "old.txt");
            File.WriteAllText(oldFilePath, "old");
            var targetPath = Path.Combine(tempRoot, "mirror", "skills");

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
            await service.ExecuteAsync(plan, allowDowngrade: true, allowMirrorAdoption: true);

            File.Delete(oldFilePath);
            File.WriteAllText(Path.Combine(sourcePath, "new.txt"), "new");

            var record = (await storage.LoadRegistryAsync()).Records.Single();
            var result = await service.SyncMirrorAsync(record, allowTargetOverwrite: false);
            var refreshedRecord = (await storage.LoadRegistryAsync()).Records.Single();

            Assert.Equal(1, result.HistoryEntry.SuccessCount);
            Assert.False(File.Exists(Path.Combine(targetPath, "old.txt")));
            Assert.True(File.Exists(Path.Combine(targetPath, "new.txt")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(targetPath, "new.txt")));
            Assert.NotNull(refreshedRecord.LastSynchronizedAt);
            Assert.NotEmpty(refreshedRecord.Targets[0].LastSynchronizedSnapshot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static LinkOperationService CreateExecutionService(string storageRoot, FakeLinkBackendService backend)
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

    private sealed class FakeLinkBackendService : ILinkBackendService
    {
        public Dictionary<LinkSourceKind, List<LinkCreationStrategy>> SupportedStrategies { get; } = [];

        public Dictionary<string, string> Failures { get; } = [];

        public Dictionary<(string TargetPath, LinkCreationStrategy Strategy), Exception> StrategyFailures { get; } = [];

        public Dictionary<string, List<LinkCreationStrategy>> AttemptedStrategies { get; } = [];

        public bool Supports(LinkSourceKind sourceKind, LinkCreationStrategy strategy)
        {
            return SupportedStrategies.TryGetValue(sourceKind, out var strategies) && strategies.Contains(strategy);
        }

        public Task CreateAsync(LinkSourceKind sourceKind, string sourcePath, string targetPath, LinkCreationStrategy strategy)
        {
            var parentDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            if (!AttemptedStrategies.TryGetValue(targetPath, out var attempted))
            {
                attempted = [];
                AttemptedStrategies[targetPath] = attempted;
            }

            attempted.Add(strategy);

            if (Failures.TryGetValue(targetPath, out var reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (StrategyFailures.TryGetValue((targetPath, strategy), out var exception))
            {
                throw exception;
            }

            if (sourceKind == LinkSourceKind.Directory)
            {
                Directory.CreateDirectory(targetPath);
            }
            else
            {
                File.WriteAllText(targetPath, $"linked:{sourcePath}:{strategy}");
            }

            return Task.CompletedTask;
        }
    }
}
