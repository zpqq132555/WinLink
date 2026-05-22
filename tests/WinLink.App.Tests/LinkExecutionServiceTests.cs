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
            var result = await service.ExecuteAsync(plan, allowDowngrade: true);
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
            var result = await service.ExecuteAsync(plan, allowDowngrade: true);

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

            if (Failures.TryGetValue(targetPath, out var reason))
            {
                throw new InvalidOperationException(reason);
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
