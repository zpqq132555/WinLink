using System.Text.Json;
using WinLink.App.Infrastructure;
using WinLink.App.Models;
using WinLink.App.Services;

namespace WinLink.App.Tests;

public sealed class UnitTest1
{
    [Fact]
    public void AppDirectories_should_derive_storage_file_paths_from_root()
    {
        var root = Path.Combine("C:\\", "Users", "Demo", "AppData", "Local", "WinLink");

        var directories = new AppDirectories(root);

        Assert.Equal(root, directories.RootDirectory);
        Assert.Equal(Path.Combine(root, "registry.json"), directories.RegistryFilePath);
        Assert.Equal(Path.Combine(root, "history.json"), directories.HistoryFilePath);
        Assert.Equal(Path.Combine(root, "ui-state.json"), directories.UiStateFilePath);
    }

    [Fact]
    public void PathEnvironmentService_should_expand_environment_variables()
    {
        Environment.SetEnvironmentVariable("WINLINK_TEST_HOME", "C:\\Temp\\WinLink");
        var service = new PathEnvironmentService();

        var expanded = service.Expand("%WINLINK_TEST_HOME%\\targets");

        Assert.Equal("C:\\Temp\\WinLink\\targets", expanded);
    }

    [Fact]
    public async Task JsonRegistryStorageService_should_return_default_registry_when_file_is_missing()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var service = new JsonRegistryStorageService(directories);

            var document = await service.LoadRegistryAsync();

            Assert.Equal(ManagedLinkRegistryDocument.CurrentSchemaVersion, document.SchemaVersion);
            Assert.Empty(document.Records);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task JsonRegistryStorageService_should_persist_registry_round_trip()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var service = new JsonRegistryStorageService(directories);
            var document = new ManagedLinkRegistryDocument
            {
                Records =
                [
                    new ManagedLinkRecord
                    {
                        Id = "task-1",
                        DisplayName = "AGENTS sync",
                        SourcePath = "C:\\repo\\AGENTS.md",
                        SourceKind = LinkSourceKind.File,
                        Targets =
                        [
                            new ManagedLinkTargetRecord
                            {
                                Id = "target-1",
                                DisplayName = "Desktop config",
                                TargetPath = "C:\\Users\\Demo\\Desktop\\AGENTS.md",
                            },
                        ],
                    },
                ],
            };

            await service.SaveRegistryAsync(document);
            var json = await File.ReadAllTextAsync(directories.RegistryFilePath);
            var restored = await service.LoadRegistryAsync();

            Assert.Contains($"\"schemaVersion\": {ManagedLinkRegistryDocument.CurrentSchemaVersion}", json);
            Assert.Single(restored.Records);
            Assert.Equal("AGENTS sync", restored.Records[0].DisplayName);
            Assert.Equal("Desktop config", restored.Records[0].Targets[0].DisplayName);
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
