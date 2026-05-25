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
    public async Task RefreshAsync_should_mark_matching_junction_as_active()
    {
        var tempRoot = CreateTemporaryDirectory();
        var targetPath = Path.Combine(tempRoot, "links", "skills");
        try
        {
            var sourcePath = Path.Combine(tempRoot, "skills");
            Directory.CreateDirectory(sourcePath);

            var backend = new WindowsLinkBackendService();
            await backend.CreateAsync(LinkSourceKind.Directory, sourcePath, targetPath, LinkCreationStrategy.Junction);

            var record = CreateRecord(sourcePath, LinkSourceKind.Directory, targetPath, LinkCreationStrategy.Junction);
            var service = new LinkStatusService();

            var refreshed = await service.RefreshAsync(record);

            Assert.Equal(LinkTargetState.Active, refreshed.Targets[0].State);
            Assert.Contains("Junction", refreshed.Targets[0].StatusReason, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath);
            }

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
    public async Task ShellViewModel_should_group_managed_links_by_display_name_source_path_and_kind()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var sharedSourcePath = Path.Combine(tempRoot, "shared-source.txt");
            var alternativeSourcePath = Path.Combine(tempRoot, "other-source.txt");
            var recordA = CreateRecord(sharedSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            recordA.Id = "record-a";
            recordA.DisplayName = "AGENTS 分发";
            recordA.Targets[0].Id = "target-a";

            var recordB = CreateRecord(sharedSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "b.txt"), LinkCreationStrategy.HardLink);
            recordB.Id = "record-b";
            recordB.DisplayName = "AGENTS 分发";
            recordB.Targets[0].Id = "target-b";

            var recordC = CreateRecord(alternativeSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "c.txt"), LinkCreationStrategy.HardLink);
            recordC.Id = "record-c";
            recordC.DisplayName = "AGENTS 分发";
            recordC.Targets[0].Id = "target-c";

            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [recordA, recordB, recordC],
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

            Assert.Equal(2, viewModel.ManagedLinks.Count);

            var groupedRecord = viewModel.ManagedLinks.Single(record => string.Equals(record.SourcePath, sharedSourcePath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, groupedRecord.Targets.Count);
            Assert.Contains(groupedRecord.Targets, target => target.Id == "target-a");
            Assert.Contains(groupedRecord.Targets, target => target.Id == "target-b");
            Assert.Equal(2, viewModel.ManagedSummary.Records);
            Assert.Equal(3, viewModel.ManagedSummary.Targets);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ShellViewModel_should_expose_reusable_managed_source_options_with_path_disambiguation()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var sharedSourcePath = Path.Combine(tempRoot, "shared-source.txt");
            var alternativeSourcePath = Path.Combine(tempRoot, "other-source.txt");
            var recordA = CreateRecord(sharedSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            recordA.Id = "record-a";
            recordA.DisplayName = "AGENTS 分发";

            var recordB = CreateRecord(sharedSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "b.txt"), LinkCreationStrategy.HardLink);
            recordB.Id = "record-b";
            recordB.DisplayName = "AGENTS 分发";

            var recordC = CreateRecord(alternativeSourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "c.txt"), LinkCreationStrategy.HardLink);
            recordC.Id = "record-c";
            recordC.DisplayName = "AGENTS 分发";

            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [recordA, recordB, recordC],
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

            Assert.Equal(2, viewModel.ReusableManagedSources.Count);
            Assert.All(viewModel.ReusableManagedSources, option => Assert.Contains(option.SourcePath, option.DisplayLabel, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(viewModel.ReusableManagedSources, option => option.SourcePath == sharedSourcePath);
            Assert.Contains(viewModel.ReusableManagedSources, option => option.SourcePath == alternativeSourcePath);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ShellViewModel_should_lock_source_fields_when_reusing_managed_source()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var sourcePath = Path.Combine(tempRoot, "shared-source.txt");
            var record = CreateRecord(sourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            record.Id = "record-a";
            record.DisplayName = "AGENTS 分发";
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [record],
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

            viewModel.SelectReusableManagedSourceForSelectedTask("record-a");

            Assert.Equal("record-a", viewModel.SelectedTaskReusableManagedSourceRecordId);
            Assert.NotNull(viewModel.SelectedTask);
            Assert.Equal("AGENTS 分发", viewModel.SelectedTask.DisplayName);
            Assert.Equal(sourcePath, viewModel.SelectedTask.SourcePath);
            Assert.Equal(LinkSourceKind.File, viewModel.SelectedTask.SourceKind);
            Assert.False(viewModel.CanEditSelectedTaskSource);
            Assert.True(viewModel.IsSelectedTaskReusingManagedSource);
            Assert.Contains("已锁定", viewModel.SelectedTaskSourceLockHint, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ShellViewModel_should_restore_manual_source_editing_after_clearing_reused_managed_source()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var sourcePath = Path.Combine(tempRoot, "shared-source.txt");
            var record = CreateRecord(sourcePath, LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            record.Id = "record-a";
            record.DisplayName = "AGENTS 分发";
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [record],
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

            viewModel.SelectReusableManagedSourceForSelectedTask("record-a");
            viewModel.ClearSelectedTaskReusableManagedSource();
            viewModel.SelectedTask!.DisplayName = "自定义备注";

            Assert.Null(viewModel.SelectedTaskReusableManagedSourceRecordId);
            Assert.False(viewModel.IsSelectedTaskReusingManagedSource);
            Assert.True(viewModel.CanEditSelectedTaskSource);
            Assert.Equal(string.Empty, viewModel.SelectedTaskSourceLockHint);
            Assert.Equal("自定义备注", viewModel.SelectedTask.DisplayName);
            Assert.Equal(sourcePath, viewModel.SelectedTask.SourcePath);
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

    [Fact]
    public async Task ApplySelectedPresetAsync_should_reuse_existing_record_when_reconnecting_disconnected_target()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var pathService = new PathEnvironmentService();
            var workbenchService = new LinkTaskWorkbenchService(pathService);
            var backend = new FakePresetApplyBackendService();
            var sourcePath = Path.Combine(tempRoot, "source", "skills");
            var targetPath = Path.Combine(tempRoot, "workspace", "skills");
            Directory.CreateDirectory(sourcePath);

            var preset = new PresetTemplateDefinition
            {
                Id = "preset-skills-sync",
                Name = "skills direct apply",
                SourcePathTemplate = sourcePath,
                Targets =
                [
                    new PresetTemplateTarget
                    {
                        DisplayName = "workspace skills",
                        TargetPathTemplate = targetPath,
                    },
                ],
            };

            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records =
                [
                    new ManagedLinkRecord
                    {
                        Id = preset.Id,
                        DisplayName = preset.Name,
                        SourcePath = sourcePath,
                        SourceKind = LinkSourceKind.Directory,
                        PreferredStrategy = LinkCreationStrategy.Junction,
                        Targets =
                        [
                            new ManagedLinkTargetRecord
                            {
                                Id = "target-1",
                                DisplayName = "workspace skills",
                                TargetPath = targetPath,
                                AppliedStrategy = LinkCreationStrategy.Junction,
                                State = LinkTargetState.Disconnected,
                                StatusReason = "disconnected",
                            },
                        ],
                    },
                ],
            });

            var viewModel = new ShellViewModel(
                directories,
                pathService,
                workbenchService,
                new LinkOperationService(pathService, workbenchService, storage, backend),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);
            viewModel.Presets.Add(preset);
            viewModel.SelectedPreset = preset;

            var plan = await viewModel.BuildSelectedPresetExecutionPlanAsync();
            await viewModel.ApplySelectedPresetAsync(plan, allowDowngrade: true);
            var registry = await storage.LoadRegistryAsync();

            var record = Assert.Single(registry.Records);
            Assert.Equal(preset.Id, record.Id);
            Assert.Single(record.Targets);
            Assert.Equal(targetPath, record.Targets[0].TargetPath);
        }
        finally
        {
            var targetPath = Path.Combine(tempRoot, "workspace", "skills");
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_should_append_target_to_existing_reused_managed_source_record()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "source", "AGENTS.md");
            var existingTargetPath = Path.Combine(tempRoot, "workspace", "existing-AGENTS.md");
            var newTargetPath = Path.Combine(tempRoot, "workspace", "new-AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "content");

            var storage = new JsonRegistryStorageService(new AppDirectories(tempRoot));
            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records =
                [
                    new ManagedLinkRecord
                    {
                        Id = "record-a",
                        DisplayName = "AGENTS 分发",
                        SourcePath = sourcePath,
                        SourceKind = LinkSourceKind.File,
                        PreferredStrategy = LinkCreationStrategy.SymbolicLink,
                        Targets =
                        [
                            new ManagedLinkTargetRecord
                            {
                                Id = "target-a",
                                DisplayName = "existing",
                                TargetPath = existingTargetPath,
                                AppliedStrategy = LinkCreationStrategy.SymbolicLink,
                                State = LinkTargetState.Disconnected,
                            },
                        ],
                    },
                ],
            });

            var service = CreateExecutionService(tempRoot, new FakePresetApplyBackendService());
            var task = new LinkTaskDraft
            {
                Id = "task-new",
                DisplayName = "AGENTS 分发",
                SourcePath = sourcePath,
                SourceKind = LinkSourceKind.File,
                PreferredStrategy = LinkCreationStrategy.SymbolicLink,
                ReusedManagedSourceRecordId = "record-a",
            };
            var newTarget = new LinkTargetDraft
            {
                DisplayName = "new",
            };
            newTarget.ApplyFullPath(newTargetPath);
            task.Targets.Add(newTarget);

            var plan = await service.PlanExecutionAsync([task]);
            var result = await service.ExecuteAsync(plan, allowDowngrade: true);
            var registry = await storage.LoadRegistryAsync();

            var record = Assert.Single(registry.Records);
            Assert.Equal("record-a", record.Id);
            Assert.Equal(2, record.Targets.Count);
            Assert.Contains(record.Targets, target => target.TargetPath == existingTargetPath);
            Assert.Contains(record.Targets, target => target.TargetPath == newTargetPath);
            Assert.True(File.Exists(newTargetPath));
            Assert.Equal(1, result.HistoryEntry.SuccessCount);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RefreshManagedLinksAsync_should_complete_on_single_threaded_context()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                directories,
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            var completed = RunOnSingleThreadedContext(
                () => _ = viewModel.RefreshManagedLinksAsync(autoTriggered: true),
                TimeSpan.FromSeconds(2),
                out var failure);

            Assert.True(completed, failure?.ToString() ?? "Calling RefreshManagedLinksAsync blocked on a single-threaded context.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildManagedTargetAsync_should_keep_current_record_and_target_selected()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var recordA = CreateRecord(Path.Combine(tempRoot, "source-a.txt"), LinkSourceKind.File, Path.Combine(tempRoot, "links", "a.txt"), LinkCreationStrategy.HardLink);
            recordA.Id = "record-a";

            var recordB = new ManagedLinkRecord
            {
                Id = "record-b",
                DisplayName = "skills 同步",
                SourcePath = Path.Combine(tempRoot, "source-b"),
                SourceKind = LinkSourceKind.Directory,
                PreferredStrategy = LinkCreationStrategy.Junction,
                Targets =
                [
                    new ManagedLinkTargetRecord
                    {
                        Id = "target-b1",
                        DisplayName = "Claude skills",
                        TargetPath = Path.Combine(tempRoot, "targets", "claude-skills"),
                        AppliedStrategy = LinkCreationStrategy.Junction,
                    },
                    new ManagedLinkTargetRecord
                    {
                        Id = "target-b2",
                        DisplayName = "Workspace skills",
                        TargetPath = Path.Combine(tempRoot, "targets", "workspace-skills"),
                        AppliedStrategy = LinkCreationStrategy.Junction,
                    },
                ],
            };

            await storage.SaveRegistryAsync(new ManagedLinkRegistryDocument
            {
                Records = [recordA, recordB],
            });
            await storage.SaveUiStateAsync(new WorkspaceUiStateDocument
            {
                SelectedManagedLinkId = "record-a",
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

            viewModel.SelectedManagedLink = viewModel.ManagedLinks.Single(record => record.Id == "record-b");
            viewModel.SelectedManagedTarget = viewModel.SelectedManagedLink.Targets.Single(target => target.Id == "target-b2");

            var reason = await viewModel.RebuildManagedTargetAsync(viewModel.SelectedManagedTarget);

            Assert.Null(reason);
            Assert.Equal("record-b", viewModel.SelectedManagedLink?.Id);
            Assert.Equal("target-b2", viewModel.SelectedManagedTarget?.Id);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShellViewModel_should_start_with_a_single_empty_pending_task()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                directories,
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            var task = Assert.Single(viewModel.PendingTasks);
            Assert.Equal("新任务", task.DisplayName);
            Assert.Equal(string.Empty, task.SourcePath);
            Assert.Equal(LinkSourceKind.Unknown, task.SourceKind);
            Assert.Empty(task.Targets);
            Assert.Same(task, viewModel.SelectedTask);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShellViewModel_should_expose_updated_agents_and_skills_presets()
    {
        var tempRoot = CreateTemporaryDirectory();
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", tempRoot);
            var directories = new AppDirectories(tempRoot);
            var storage = new JsonRegistryStorageService(directories);
            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                directories,
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            Assert.Collection(
                viewModel.Presets,
                agentsPreset =>
                {
                    Assert.Equal("AGENTS 主配置分发", agentsPreset.Name);
                    Assert.Equal("%USERPROFILE%\\.agents\\skills\\AGENTS_BAK.md", agentsPreset.SourcePathTemplate);
                    Assert.Collection(
                        agentsPreset.Targets,
                        codexTarget =>
                        {
                            Assert.Equal("Codex AGENTS", codexTarget.DisplayName);
                            Assert.Equal("%USERPROFILE%\\.codex\\AGENTS.md", codexTarget.TargetPathTemplate);
                        },
                        claudeTarget =>
                        {
                            Assert.Equal("Claude CLAUDE", claudeTarget.DisplayName);
                            Assert.Equal("%USERPROFILE%\\.claude\\CLAUDE.md", claudeTarget.TargetPathTemplate);
                        });
                },
                skillsPreset =>
                {
                    Assert.Equal("skills 目录同步", skillsPreset.Name);
                    Assert.Equal("%USERPROFILE%\\.agents\\skills", skillsPreset.SourcePathTemplate);
                    var target = Assert.Single(skillsPreset.Targets);
                    Assert.Equal("Claude skills", target.DisplayName);
                    Assert.Equal("%USERPROFILE%\\.claude\\skills", target.TargetPathTemplate);
                });

            Assert.Equal(Path.Combine(tempRoot, ".agents", "skills", "AGENTS_BAK.md"), viewModel.SelectedPresetPreview?.ExpandedSourcePath);
            Assert.Collection(
                viewModel.SelectedPresetPreview?.Targets ?? [],
                codexTarget => Assert.Equal(Path.Combine(tempRoot, ".codex", "AGENTS.md"), codexTarget.ExpandedTargetPath),
                claudeTarget => Assert.Equal(Path.Combine(tempRoot, ".claude", "CLAUDE.md"), claudeTarget.ExpandedTargetPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Selecting_managed_link_should_not_block_when_ui_state_save_is_slow()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var storage = new BlockingUiStateStorage();
            storage.Registry.Records.Add(CreateRecord(
                Path.Combine(tempRoot, "source-a.txt"),
                LinkSourceKind.File,
                Path.Combine(tempRoot, "links", "a.txt"),
                LinkCreationStrategy.HardLink));
            storage.Registry.Records.Add(CreateRecord(
                Path.Combine(tempRoot, "source-b.txt"),
                LinkSourceKind.File,
                Path.Combine(tempRoot, "links", "b.txt"),
                LinkCreationStrategy.HardLink));

            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                new AppDirectories(tempRoot),
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            var completed = RunOnSingleThreadedContext(
                () => viewModel.SelectedManagedLink = viewModel.ManagedLinks.Last(),
                TimeSpan.FromSeconds(2),
                out var failure);

            Assert.True(completed, failure?.ToString() ?? "Selecting a managed link blocked on UI state persistence.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Expanding_managed_link_should_not_block_when_ui_state_save_is_slow()
    {
        var tempRoot = CreateTemporaryDirectory();
        try
        {
            var storage = new BlockingUiStateStorage();
            var record = CreateRecord(
                Path.Combine(tempRoot, "source-a.txt"),
                LinkSourceKind.File,
                Path.Combine(tempRoot, "links", "a.txt"),
                LinkCreationStrategy.HardLink);
            storage.Registry.Records.Add(record);

            var pathService = new PathEnvironmentService();
            var viewModel = new ShellViewModel(
                new AppDirectories(tempRoot),
                pathService,
                new LinkTaskWorkbenchService(pathService),
                new NoopLinkOperationService(),
                new NoopLinkStatusService(),
                new PresetTemplateService(pathService, tempRoot),
                storage);

            var completed = RunOnSingleThreadedContext(
                () => viewModel.SetManagedLinkExpanded(viewModel.SelectedManagedLink, true),
                TimeSpan.FromSeconds(2),
                out var failure);

            Assert.True(completed, failure?.ToString() ?? "Expanding managed link details blocked on UI state persistence.");
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

    private static bool RunOnSingleThreadedContext(Action action, TimeSpan timeout, out Exception? failure)
    {
        Exception? capturedFailure = null;
        using var completed = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
                action();
            }
            catch (Exception ex)
            {
                capturedFailure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        var didComplete = completed.Wait(timeout);
        failure = capturedFailure;
        return didComplete;
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
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

    private sealed class BlockingUiStateStorage : IRegistryStorageService
    {
        public ManagedLinkRegistryDocument Registry { get; } = new();

        public ExecutionHistoryDocument History { get; } = new();

        public WorkspaceUiStateDocument UiState { get; } = new();

        public Task<ManagedLinkRegistryDocument> LoadRegistryAsync()
        {
            return Task.FromResult(Registry);
        }

        public Task SaveRegistryAsync(ManagedLinkRegistryDocument document)
        {
            Registry.Records = document.Records;
            return Task.CompletedTask;
        }

        public Task<ExecutionHistoryDocument> LoadHistoryAsync()
        {
            return Task.FromResult(History);
        }

        public Task SaveHistoryAsync(ExecutionHistoryDocument document)
        {
            History.Entries = document.Entries;
            return Task.CompletedTask;
        }

        public Task<WorkspaceUiStateDocument> LoadUiStateAsync()
        {
            return Task.FromResult(UiState);
        }

        public Task SaveUiStateAsync(WorkspaceUiStateDocument document)
        {
            return new TaskCompletionSource().Task;
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
