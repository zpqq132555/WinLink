using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.Tests;

public sealed class PresetTemplateServiceTests
{
    [Fact]
    public void BuildPreview_should_expand_paths_and_mark_conflicts()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            Environment.SetEnvironmentVariable("WINLINK_PRESET_HOME", tempRoot);
            var sourcePath = Path.Combine(tempRoot, "AGENTS.md");
            File.WriteAllText(sourcePath, "test");
            var conflictTarget = Path.Combine(tempRoot, "workspace", "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(conflictTarget)!);
            File.WriteAllText(conflictTarget, "existing");

            var preset = new PresetTemplateDefinition
            {
                Name = "AGENTS 主配置分发",
                SourcePathTemplate = "%WINLINK_PRESET_HOME%\\AGENTS.md",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "workspace",
                        TargetPathTemplate = "%WORKSPACE%\\AGENTS.md",
                    },
                ],
            };

            var service = new PresetTemplateService(new PathEnvironmentService(), Path.Combine(tempRoot, "workspace"));

            var preview = service.BuildPreview(preset);

            Assert.Equal(sourcePath, preview.ExpandedSourcePath);
            Assert.Single(preview.Targets);
            Assert.Equal(conflictTarget, preview.Targets[0].ExpandedTargetPath);
            Assert.True(preview.Targets[0].HasConflict);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void BuildPreview_should_mark_missing_parent_directory()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            Environment.SetEnvironmentVariable("WINLINK_PRESET_HOME", tempRoot);
            var preset = new PresetTemplateDefinition
            {
                Name = "skills 目录同步",
                SourcePathTemplate = "%WINLINK_PRESET_HOME%\\skills",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "workspace skills",
                        TargetPathTemplate = "%WORKSPACE%\\nested\\skills",
                    },
                ],
            };

            var service = new PresetTemplateService(new PathEnvironmentService(), Path.Combine(tempRoot, "workspace"));

            var preview = service.BuildPreview(preset);

            Assert.True(preview.Targets[0].ParentDirectoryMissing);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void InstantiateTask_should_convert_preset_into_editable_workbench_task()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            Environment.SetEnvironmentVariable("WINLINK_PRESET_HOME", tempRoot);
            var preset = new PresetTemplateDefinition
            {
                Name = "skills 目录同步",
                SourcePathTemplate = "%WINLINK_PRESET_HOME%\\skills",
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "workspace skills",
                        TargetPathTemplate = "%WORKSPACE%\\.codex\\skills",
                    },
                ],
            };

            var service = new PresetTemplateService(new PathEnvironmentService(), Path.Combine(tempRoot, "workspace"));

            var task = service.InstantiateTask(preset);

            Assert.Equal("skills 目录同步", task.DisplayName);
            Assert.Equal(Path.Combine(tempRoot, "skills"), task.SourcePath);
            Assert.Single(task.Targets);
            Assert.Equal(Path.Combine(tempRoot, "workspace", ".codex", "skills"), task.Targets[0].TargetPath);
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
}
