using WinLink.App.Infrastructure;
using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.Tests;

public sealed class LinkTaskWorkbenchServiceTests
{
    [Fact]
    public void ApplySource_should_lock_file_task_and_fill_default_display_name()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();

            service.ApplySource(task, sourcePath);

            Assert.Equal(LinkSourceKind.File, task.SourceKind);
            Assert.Equal("AGENTS.md", task.DisplayName);
            Assert.Equal(sourcePath, task.SourcePath);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AddTargetFromDirectory_should_use_source_leaf_name_as_default_target_name()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            var targetDirectory = Path.Combine(tempRoot, "targets");
            Directory.CreateDirectory(targetDirectory);

            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();
            service.ApplySource(task, sourcePath);

            var target = service.AddTargetFromDirectory(task, targetDirectory);

            Assert.Equal("skills", target.DisplayName);
            Assert.Equal(Path.Combine(targetDirectory, "skills"), target.TargetPath);
            Assert.Equal(LinkTargetInputMode.DirectoryWithName, target.InputMode);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AddTargetFromFullPath_should_preserve_explicit_target_path()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var explicitTargetPath = Path.Combine(tempRoot, "custom", "agent-copy.md");

            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();
            service.ApplySource(task, sourcePath);

            var target = service.AddTargetFromFullPath(task, explicitTargetPath);

            Assert.Equal(explicitTargetPath, target.TargetPath);
            Assert.Equal("agent-copy.md", target.DisplayName);
            Assert.Equal(LinkTargetInputMode.FullPath, target.InputMode);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DirectoryMode_target_should_recompute_target_path_when_name_changes()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);
            var targetDirectory = Path.Combine(tempRoot, "targets");
            Directory.CreateDirectory(targetDirectory);

            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();
            service.ApplySource(task, sourcePath);
            var target = service.AddTargetFromDirectory(task, targetDirectory);

            target.DisplayName = "skills-prod";

            Assert.Equal(Path.Combine(targetDirectory, "skills-prod"), target.TargetPath);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RunLightValidation_should_flag_existing_target_conflict()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var targetPath = Path.Combine(tempRoot, "existing", "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "existing");

            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();
            service.ApplySource(task, sourcePath);
            var target = service.AddTargetFromFullPath(task, targetPath);

            service.RunLightValidation(task);

            Assert.Contains("已存在", target.ValidationMessage);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RemoveTarget_should_delete_selected_target_from_task()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var service = new LinkTaskWorkbenchService(new PathEnvironmentService());
            var task = new LinkTaskDraft();
            service.ApplySource(task, sourcePath);
            var target = service.AddTargetFromFullPath(task, Path.Combine(tempRoot, "copy", "AGENTS.md"));

            service.RemoveTarget(task, target);

            Assert.Empty(task.Targets);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_should_report_missing_source_and_conflicting_target()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var conflictDirectory = Path.Combine(tempRoot, "target");
            Directory.CreateDirectory(conflictDirectory);
            var conflictPath = Path.Combine(conflictDirectory, "missing.txt");
            File.WriteAllText(conflictPath, "existing");

            var task = new LinkTaskDraft
            {
                DisplayName = "missing source",
            };
            task.SourcePath = Path.Combine(tempRoot, "missing.txt");
            task.SourceKind = LinkSourceKind.File;
            task.Targets.Add(new LinkTargetDraft());
            task.Targets[0].ApplyFullPath(conflictPath);

            var pathService = new PathEnvironmentService();
            var service = new LinkOperationService(
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new JsonRegistryStorageService(new AppDirectories(tempRoot)),
                new FakeLinkBackendService());

            var issues = await service.ValidateAsync([task]);

            Assert.Contains(issues, issue => issue.Contains("源不存在", StringComparison.Ordinal));
            Assert.Contains(issues, issue => issue.Contains("目标已存在", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinLink.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeLinkBackendService : ILinkBackendService
    {
        public bool Supports(LinkSourceKind sourceKind, LinkCreationStrategy strategy)
        {
            return sourceKind switch
            {
                LinkSourceKind.File => strategy is LinkCreationStrategy.SymbolicLink or LinkCreationStrategy.HardLink,
                LinkSourceKind.Directory => strategy is LinkCreationStrategy.SymbolicLink or LinkCreationStrategy.Junction,
                _ => false,
            };
        }

        public Task CreateAsync(LinkSourceKind sourceKind, string sourcePath, string targetPath, LinkCreationStrategy strategy)
        {
            return Task.CompletedTask;
        }
    }
}
