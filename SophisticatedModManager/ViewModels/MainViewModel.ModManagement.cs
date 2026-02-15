using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.Input;
using SharpCompress.Archives;
using SharpCompress.Common;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Mod management (add, delete, move, duplicate, config, backups).
/// </summary>
public partial class MainViewModel
{
    private void AddMods()
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        var profileModDir = ProfileService.GetActiveProfileModPath(SelectedProfile.Name, ModsRootPath);
        if (!Directory.Exists(profileModDir))
            Directory.CreateDirectory(profileModDir);

        ExtractAndAddMods(profileModDir, $"profile \"{SelectedProfile.Name}\"");
    }

    [RelayCommand]
    private void AddCommonMods()
    {
        if (!Directory.Exists(ModsRootPath))
            Directory.CreateDirectory(ModsRootPath);

        ExtractAndAddMods(ModsRootPath, "Common Mods");
    }

    private void ExtractAndAddMods(string targetDir, string targetLabel)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Select Mod Archive(s) to Add to {targetLabel}",
            Filter = "Archives (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var files = dialog.FileNames;

        // Track results across all archives for a combined status message
        int totalSimpleAdded = 0;
        var allTempDirs = new List<string>();
        var allExtractedFolders = new List<ExtractedFolderItem>();
        string? lastPendingTempDir = null;

        foreach (var file in files)
        {
            try
            {
                var tempDir = FileSystemHelpers.ExtractArchive(file);
                var topDirs = Directory.GetDirectories(tempDir);

                // Simple case: single mod folder with manifest — install directly
                if (topDirs.Length == 1 && File.Exists(Path.Combine(topDirs[0], "manifest.json")))
                {
                    BackupSavesBeforeModInstall(targetDir);
                    var sourceDir = topDirs[0];
                    var destDir = Path.Combine(targetDir, Path.GetFileName(sourceDir));
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, true);
                    FileSystemHelpers.CopyDirectory(sourceDir, destDir);
                    CleanupTempDir(tempDir);
                    totalSimpleAdded++;
                    continue;
                }

                // Collection case: single top-level folder containing multiple mods
                if (topDirs.Length == 1 && !File.Exists(Path.Combine(topDirs[0], "manifest.json")))
                {
                    var subModFolders = FindModFolders(topDirs[0]);
                    if (subModFolders.Count >= 2)
                    {
                        BackupSavesBeforeModInstall(targetDir);
                        var sourceDir = topDirs[0];
                        var collectionName = Path.GetFileName(sourceDir);
                        var destDir = Path.Combine(targetDir, collectionName);
                        if (Directory.Exists(destDir))
                            Directory.Delete(destDir, true);
                        FileSystemHelpers.CopyDirectory(sourceDir, destDir);
                        RegisterCollection(collectionName, targetDir);
                        CleanupTempDir(tempDir);
                        totalSimpleAdded++;
                        continue;
                    }
                }

                // Complex case: collect folders for user selection
                allTempDirs.Add(tempDir);
                lastPendingTempDir = tempDir;

                var modFolders = FindModFolders(tempDir);

                if (modFolders.Count == 0)
                {
                    foreach (var dir in topDirs)
                    {
                        allExtractedFolders.Add(new ExtractedFolderItem
                        {
                            Name = Path.GetFileName(dir),
                            FullPath = dir,
                            HasManifest = false,
                            IsSelected = false
                        });
                    }
                    var topFiles = Directory.GetFiles(tempDir);
                    if (topFiles.Length > 0 && topDirs.Length == 0)
                    {
                        CleanupTempDir(tempDir);
                        allTempDirs.Remove(tempDir);
                    }
                }
                else
                {
                    foreach (var modDir in modFolders)
                    {
                        var relativePath = Path.GetRelativePath(tempDir, modDir);
                        allExtractedFolders.Add(new ExtractedFolderItem
                        {
                            Name = Path.GetFileName(modDir),
                            RelativePath = relativePath,
                            FullPath = modDir,
                            HasManifest = true,
                            IsSelected = modFolders.Count == 1
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to extract {Path.GetFileName(file)}: {ex.Message}";
            }
        }

        // If we directly added simple mods, refresh
        if (totalSimpleAdded > 0 && allExtractedFolders.Count == 0)
        {
            StatusMessage = $"Added {totalSimpleAdded} mod(s) to {targetLabel}.";
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            return;
        }

        // If there are folders needing user selection, show the picker
        if (allExtractedFolders.Count > 0)
        {
            _pendingExtractTempDirs = allTempDirs;
            _pendingProfileModDir = targetDir;
            ExtractedArchiveName = files.Length == 1
                ? Path.GetFileNameWithoutExtension(files[0])
                : $"{files.Length} archives";
            ExtractedFolders.Clear();
            foreach (var f in allExtractedFolders)
                ExtractedFolders.Add(f);

            IsSelectingModFolders = true;
            var prefix = totalSimpleAdded > 0
                ? $"Added {totalSimpleAdded} mod(s) directly. "
                : "";
            StatusMessage = $"{prefix}Select which folder(s) to add from the archive(s).";
        }
        else if (totalSimpleAdded == 0)
        {
            StatusMessage = "No mod folders found in the selected archive(s).";
        }
    }

    [RelayCommand]
    private void ConfirmAddSelectedFolders()
    {
        if (_pendingProfileModDir == null) return;

        var selected = ExtractedFolders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one folder to add.";
            return;
        }

        BackupSavesBeforeModInstall(_pendingProfileModDir);

        int added = 0;
        foreach (var folder in selected)
        {
            try
            {
                var destDir = Path.Combine(_pendingProfileModDir, folder.Name);
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, true);
                FileSystemHelpers.CopyDirectory(folder.FullPath, destDir);
                added++;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add \"{folder.Name}\": {ex.Message}";
            }
        }

        if (_pendingExtractTempDirs != null)
            foreach (var dir in _pendingExtractTempDirs)
                CleanupTempDir(dir);
        _pendingExtractTempDirs = null;
        _pendingProfileModDir = null;
        IsSelectingModFolders = false;
        ExtractedFolders.Clear();

        if (added > 0)
        {
            StatusMessage = $"Added {added} mod(s) to profile \"{SelectedProfile?.Name}\".";
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
    }

    [RelayCommand]
    private void CancelAddMods()
    {
        if (_pendingExtractTempDirs != null)
            foreach (var dir in _pendingExtractTempDirs)
                CleanupTempDir(dir);
        _pendingExtractTempDirs = null;
        _pendingProfileModDir = null;
        IsSelectingModFolders = false;
        ExtractedFolders.Clear();
        StatusMessage = "Cancelled adding mods.";
    }

    private static List<string> FindModFolders(string root)
    {
        var results = new List<string>();
        FindModFoldersRecursive(root, results);
        return results;
    }

    private static void FindModFoldersRecursive(string dir, List<string> results)
    {
        if (File.Exists(Path.Combine(dir, "manifest.json")))
        {
            results.Add(dir);
            return;
        }

        foreach (var subDir in Directory.GetDirectories(dir))
            FindModFoldersRecursive(subDir, results);
    }

    private static void CleanupTempDir(string? tempDir)
    {
        try
        {
            if (tempDir != null && Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch { /* best effort cleanup */ }
    }

    private void RegisterCollection(string collectionName, string targetDir)
    {
        bool isCommon = string.Equals(
            Path.GetFullPath(targetDir),
            Path.GetFullPath(ModsRootPath),
            StringComparison.OrdinalIgnoreCase);

        if (isCommon)
        {
            if (!_config.CommonCollectionNames.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                _config.CommonCollectionNames.Add(collectionName);
        }
        else if (SelectedProfile != null)
        {
            if (!_config.ProfileCollectionNames.TryGetValue(SelectedProfile.Name, out var list))
            {
                list = new List<string>();
                _config.ProfileCollectionNames[SelectedProfile.Name] = list;
            }
            if (!list.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                list.Add(collectionName);
        }
        _configService.Save(_config);
    }

    [RelayCommand]
    private void RequestDeleteMod(ModEntryViewModel modVm)
    {
        ModPendingDelete = modVm;
        IsConfirmingModDelete = true;
    }

    [RelayCommand]
    private void ConfirmDeleteMod()
    {
        if (ModPendingDelete == null) return;
        try
        {
            _modService.DeleteMod(ModPendingDelete.Model);

            if (ModPendingDelete.IsCollection)
            {
                var collectionName = FolderNameHelper.GetCleanName(ModPendingDelete.Model.FolderName);
                _config.CommonCollectionNames.Remove(collectionName);
                foreach (var list in _config.ProfileCollectionNames.Values)
                    list.Remove(collectionName);
                _configService.Save(_config);
            }

            IsConfirmingModDelete = false;
            ModPendingDelete = null;
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = "Mod deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete mod: {ex.Message}";
            IsConfirmingModDelete = false;
            ModPendingDelete = null;
        }
    }

    [RelayCommand]
    private void CancelDeleteMod()
    {
        IsConfirmingModDelete = false;
        ModPendingDelete = null;
    }

    [RelayCommand]
    private void OpenConfigEditor(ModEntryViewModel? modVm)
    {
        if (modVm?.Model.FolderPath == null) return;

        try
        {
            var entries = _modConfigService.LoadConfig(modVm.Model.FolderPath);
            ConfigEntries.Clear();
            foreach (var entry in entries)
                ConfigEntries.Add(new ConfigEntryViewModel(entry));

            ConfigModName = modVm.Name;
            _configModFolderPath = modVm.Model.FolderPath;
            ConfigSaveStatus = string.Empty;
            IsEditingConfig = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load config: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveConfigEditor()
    {
        if (_configModFolderPath == null) return;

        try
        {
            foreach (var entryVm in ConfigEntries)
                entryVm.ApplyToModel();

            var entries = ConfigEntries.Select(e => e.Model).ToList();
            _modConfigService.SaveConfig(_configModFolderPath, entries);

            ConfigSaveStatus = "Saved successfully.";
            StatusMessage = $"Config saved for \"{ConfigModName}\".";
        }
        catch (Exception ex)
        {
            ConfigSaveStatus = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseConfigEditor()
    {
        IsEditingConfig = false;
        ConfigEntries.Clear();
        ConfigModName = string.Empty;
        _configModFolderPath = null;
        ConfigSaveStatus = string.Empty;
    }

    [RelayCommand]
    private void MoveMod(MoveModRequest request)
    {
        if (request.Mod == null) return;
        try
        {
            string targetDir;
            if (request.TargetType == "Common")
            {
                targetDir = ModsRootPath;
            }
            else
            {
                var profileDir = ProfileService.GetActiveProfileModPath(request.TargetProfileName!, ModsRootPath);
                if (!Directory.Exists(profileDir))
                    profileDir = ProfileService.GetInactiveProfileModPath(request.TargetProfileName!, ModsRootPath);
                targetDir = profileDir;
            }

            _modService.MoveMod(request.Mod.Model, targetDir);

            if (request.Mod.IsCollection)
            {
                var collectionName = FolderNameHelper.GetCleanName(request.Mod.Model.FolderName);

                if (request.Mod.IsCommon)
                {
                    _config.CommonCollectionNames.Remove(collectionName);
                }
                else if (SelectedProfile != null &&
                         _config.ProfileCollectionNames.TryGetValue(SelectedProfile.Name, out var sourceList))
                {
                    sourceList.Remove(collectionName);
                }

                if (request.TargetType == "Common")
                {
                    if (!_config.CommonCollectionNames.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                        _config.CommonCollectionNames.Add(collectionName);
                }
                else
                {
                    var targetProfile = request.TargetProfileName!;
                    if (!_config.ProfileCollectionNames.ContainsKey(targetProfile))
                        _config.ProfileCollectionNames[targetProfile] = new List<string>();
                    var targetList = _config.ProfileCollectionNames[targetProfile];
                    if (!targetList.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                        targetList.Add(collectionName);
                }

                _configService.Save(_config);
            }

            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Moved \"{request.Mod.Name}\" to {(request.TargetType == "Common" ? "Common Mods" : request.TargetProfileName)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to move mod: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DuplicateMod(DuplicateModRequest request)
    {
        if (request.Mod == null) return;
        try
        {
            var profileDir = ProfileService.GetActiveProfileModPath(request.TargetProfileName, ModsRootPath);
            if (!Directory.Exists(profileDir))
                profileDir = ProfileService.GetInactiveProfileModPath(request.TargetProfileName, ModsRootPath);

            _modService.DuplicateMod(request.Mod.Model, profileDir);

            // If duplicating a collection, register it in the target profile's config
            if (request.Mod.IsCollection)
            {
                var collectionName = FolderNameHelper.GetCleanName(request.Mod.Model.FolderName);
                if (!_config.ProfileCollectionNames.ContainsKey(request.TargetProfileName))
                    _config.ProfileCollectionNames[request.TargetProfileName] = new List<string>();
                var targetList = _config.ProfileCollectionNames[request.TargetProfileName];
                if (!targetList.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                    targetList.Add(collectionName);
                _configService.Save(_config);
            }

            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Duplicated \"{request.Mod.Name}\" to \"{request.TargetProfileName}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to duplicate mod: {ex.Message}";
        }
    }

    private void BackupSavesBeforeModInstall(string targetDir)
    {
        if (_config.MaxSaveBackups <= 0) return;

        if (string.Equals(targetDir, ModsRootPath, StringComparison.OrdinalIgnoreCase))
        {
            // Common mod -> backup ALL profiles
            _saveBackupService.BackupAllProfileSaves(
                _config.ProfileNames, _config.SavesPath, _config.ActiveProfileName, _config.MaxSaveBackups);
        }
        else
        {
            // Profile mod -> backup that profile
            var profileName = ProfileService.ParseProfileName(Path.GetFileName(targetDir));
            if (profileName != null)
                _saveBackupService.BackupProfileSaves(
                    profileName, _config.SavesPath, _config.ActiveProfileName, _config.MaxSaveBackups);
        }
    }

    [RelayCommand]
    private void OpenBackups()
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        var backups = _saveBackupService.GetBackupsForProfile(SelectedProfile.Name, _config.SavesPath);
        var now = DateTime.Now;

        BackupItems.Clear();
        foreach (var b in backups)
        {
            BackupItems.Add(new BackupDisplayItem
            {
                BackupInfo = b,
                SaveNameDisplay = b.SaveName,
                TimestampDisplay = b.Timestamp.ToString("MMM d, yyyy  h:mm tt"),
                RelativeTimeDisplay = FormatRelativeTime(now - b.Timestamp),
                SizeDisplay = FormatFileSize(b.SizeBytes)
            });
        }

        var currentTs = _saveBackupService.GetCurrentSaveTimestamp(
            SelectedProfile.Name, _config.SavesPath, _config.ActiveProfileName);
        CurrentSaveTimestampDisplay = currentTs.HasValue
            ? $"Current save: {currentTs.Value:MMM d, yyyy  h:mm tt}"
            : "No saves found for this profile.";

        SelectedBackup = null;
        IsConfirmingRestore = false;
        IsViewingBackups = true;
    }

    [RelayCommand]
    private void CloseBackups()
    {
        IsViewingBackups = false;
        IsConfirmingRestore = false;
        IsRestoreAllMode = false;
        RestoreAllItems.Clear();
        SelectedBackup = null;
    }

    [RelayCommand]
    private void SelectBackupForRestore(BackupDisplayItem item)
    {
        SelectedBackup = item;
        IsConfirmingRestore = true;
    }

    [RelayCommand]
    private void ConfirmRestoreBackup()
    {
        if (SelectedBackup == null) return;

        try
        {
            _saveBackupService.RestoreBackup(
                SelectedBackup.BackupInfo, _config.SavesPath, _config.ActiveProfileName);
            StatusMessage = $"Restored \"{SelectedBackup.SaveNameDisplay}\" from {SelectedBackup.TimestampDisplay}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to restore backup: {ex.Message}";
        }

        IsViewingBackups = false;
        IsConfirmingRestore = false;
        IsRestoreAllMode = false;
        RestoreAllItems.Clear();
        SelectedBackup = null;
    }

    [RelayCommand]
    private void CancelRestore()
    {
        IsConfirmingRestore = false;
        SelectedBackup = null;
    }

    [RelayCommand]
    private void OpenRestoreAll()
    {
        if (BackupItems.Count == 0) return;

        // Group backups by save name, take the latest per save
        var latestPerSave = BackupItems
            .GroupBy(b => b.SaveNameDisplay)
            .Select(g => g.OrderByDescending(b => b.BackupInfo.Timestamp).First())
            .OrderBy(b => b.SaveNameDisplay)
            .ToList();

        RestoreAllItems.Clear();
        foreach (var item in latestPerSave)
        {
            RestoreAllItems.Add(new RestoreAllItem
            {
                IsSelected = true,
                SaveName = item.SaveNameDisplay,
                LatestBackup = item.BackupInfo,
                TimestampDisplay = item.TimestampDisplay,
                SizeDisplay = item.SizeDisplay
            });
        }

        IsConfirmingRestore = false;
        SelectedBackup = null;
        IsRestoreAllMode = true;
    }

    [RelayCommand]
    private void ConfirmRestoreAll()
    {
        var selected = RestoreAllItems.Where(r => r.IsSelected).Select(r => r.LatestBackup).ToList();
        if (selected.Count == 0) return;

        try
        {
            _saveBackupService.RestoreSaves(selected, _config.SavesPath, _config.ActiveProfileName);
            StatusMessage = $"Restored {selected.Count} save(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to restore saves: {ex.Message}";
        }

        IsViewingBackups = false;
        IsConfirmingRestore = false;
        IsRestoreAllMode = false;
        RestoreAllItems.Clear();
        SelectedBackup = null;
    }

    [RelayCommand]
    private void CancelRestoreAll()
    {
        IsRestoreAllMode = false;
        RestoreAllItems.Clear();
    }

    private static string FormatRelativeTime(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

}
