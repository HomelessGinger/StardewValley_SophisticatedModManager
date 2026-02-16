using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using SophisticatedModManager.Models;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Shared mods feature (share across profiles, update prompts, duplicate detection).
/// </summary>
public partial class MainViewModel
{
    // ===== Shared Mods Commands =====

    private void ValidateSharedPoolOnStartup()
    {
        if (_config.SharedMods.Count == 0) return;

        try
        {
            var broken = _sharedModService.ValidateSharedPool(ModsRootPath, _config);
            foreach (var entry in broken)
                _sharedModService.RepairBrokenSharedMod(entry, ModsRootPath, _config);

            if (broken.Count > 0)
                _configService.Save(_config);
        }
        catch
        {
        }
    }

    public void OpenShareDialog(ModEntryViewModel modVm)
    {
        ModToShare = modVm;
        ShareTargetProfiles.Clear();

        foreach (var profile in _config.ProfileNames)
        {
            var hasModInProfile = false;
            string modVersion = "";
            var actualDir = FileSystemHelpers.FindProfileDir(profile, ModsRootPath);

            if (actualDir != null)
            {
                var cleanName = FolderNameHelper.GetCleanName(modVm.Model.FolderName);
                var modPath = Path.Combine(actualDir, cleanName);
                var modPathDisabled = Path.Combine(actualDir, FolderNameHelper.ToDisabled(cleanName));

                if (Directory.Exists(modPath) || Directory.Exists(modPathDisabled))
                {
                    hasModInProfile = true;
                    var manifestPath = Path.Combine(
                        Directory.Exists(modPath) ? modPath : modPathDisabled,
                        "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var entry = FileSystemHelpers.ReadManifestBasic(manifestPath);
                        if (entry != null)
                            modVersion = entry.Version;
                    }
                }
            }

            if (hasModInProfile)
            {
                ShareTargetProfiles.Add(new ShareTargetProfile
                {
                    ProfileName = profile,
                    IsSelected = true,
                    CurrentVersion = modVersion,
                    HasVersionMismatch = !string.IsNullOrEmpty(modVersion) &&
                                        !string.IsNullOrEmpty(modVm.Version) &&
                                        modVersion != modVm.Version
                });
            }
        }

        IsShowingShareDialog = true;
    }

    [RelayCommand]
    private void ConfirmShareMod()
    {
        if (ModToShare == null) return;

        var selectedProfiles = ShareTargetProfiles
            .Where(p => p.IsSelected)
            .Select(p => p.ProfileName)
            .ToList();

        if (selectedProfiles.Count < 2)
        {
            StatusMessage = ModToShare.IsCollection
                ? "Select at least 2 profiles to share a collection."
                : "Select at least 2 profiles to share a mod.";
            return;
        }

        try
        {
            if (ModToShare.IsCollection)
            {
                _sharedModService.ShareCollection(ModToShare.Model, selectedProfiles, ModsRootPath, _config);
                StatusMessage = "Collection shared successfully across profiles.";
            }
            else
            {
                _sharedModService.ShareMod(ModToShare.Model, selectedProfiles, ModsRootPath, _config);
                StatusMessage = "Mod shared successfully across profiles.";
            }

            _configService.Save(_config);
            IsShowingShareDialog = false;
            ModToShare = null;
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to share {(ModToShare?.IsCollection == true ? "collection" : "mod")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelShareMod()
    {
        IsShowingShareDialog = false;
        ModToShare = null;
    }

    public void UnshareMod(ModEntryViewModel modVm)
    {
        if (modVm.SharedFolderName == null || SelectedProfile == null) return;

        try
        {
            _sharedModService.UnshareMod(modVm.SharedFolderName, SelectedProfile.Name, ModsRootPath, _config);
            _configService.Save(_config);
            LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Unshared \"{modVm.Name}\" for this profile.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to unshare mod: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConfirmSharedModUpdateForAll()
    {
        if (SharedModUpdatePendingMod == null) return;

        var mod = SharedModUpdatePendingMod;
        IsPromptingSharedModUpdate = false;
        SharedModUpdatePendingMod = null;

        if (mod.NexusModId == null) return;

        // Update directly in the shared pool folder
        var sharedFolderName = mod.SharedFolderName;
        if (sharedFolderName == null) return;

        var sharedModPath = Path.Combine(ModsRootPath, ISharedModService.SharedPoolFolderName, sharedFolderName);

        mod.IsUpdating = true;
        try
        {
            await DownloadAndInstallFromNexus(mod.NexusModId.Value, sharedModPath);
            mod.HasUpdate = false;
            mod.IsUpdating = false;
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Updated \"{mod.Name}\" for all profiles.";
        }
        catch (Exception ex)
        {
            mod.IsUpdating = false;
            StatusMessage = $"Failed to update \"{mod.Name}\": {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UnshareAndUpdateThisProfile()
    {
        if (SharedModUpdatePendingMod == null || SelectedProfile == null) return;

        var mod = SharedModUpdatePendingMod;
        IsPromptingSharedModUpdate = false;
        SharedModUpdatePendingMod = null;

        if (mod.NexusModId == null || mod.SharedFolderName == null) return;

        try
        {
            // Unshare first
            _sharedModService.UnshareMod(mod.SharedFolderName, SelectedProfile.Name, ModsRootPath, _config);
            _configService.Save(_config);

            // Now update the local copy
            var profileDir = FileSystemHelpers.FindProfileDir(SelectedProfile.Name, ModsRootPath);
            if (profileDir == null)
            {
                mod.IsUpdating = false;
                StatusMessage = $"Profile directory not found for {SelectedProfile.Name}";
                return;
            }

            var localModPath = Path.Combine(profileDir, FolderNameHelper.GetCleanName(mod.Model.FolderName));

            mod.IsUpdating = true;
            await DownloadAndInstallFromNexus(mod.NexusModId.Value, localModPath);
            mod.HasUpdate = false;
            mod.IsUpdating = false;
            LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Unshared and updated \"{mod.Name}\" for this profile.";
        }
        catch (Exception ex)
        {
            mod.IsUpdating = false;
            StatusMessage = $"Failed to update \"{mod.Name}\": {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelSharedModUpdate()
    {
        IsPromptingSharedModUpdate = false;
        SharedModUpdatePendingMod = null;
    }

    [RelayCommand]
    private async Task DetectDuplicates()
    {
        try
        {
            // Detect duplicate mods
            var duplicates = _sharedModService.DetectDuplicateMods(
                _config.ProfileNames, ModsRootPath, _config);

            DetectedDuplicates.Clear();
            foreach (var (uid, entries) in duplicates)
            {
                var first = entries[0].Mod;
                var versions = entries.Select(e => e.Mod.Version).Distinct().ToList();
                var group = new DuplicateModGroup
                {
                    ModName = first.Name,
                    UniqueID = uid,
                    HasVersionMismatch = versions.Count > 1,
                    Instances = new ObservableCollection<DuplicateModInstance>(
                        entries.Select(e => new DuplicateModInstance
                        {
                            ProfileName = e.ProfileName,
                            Version = e.Mod.Version
                        }))
                };
                DetectedDuplicates.Add(group);
            }

            // Detect duplicate collections
            var duplicateCollections = await _sharedModService.DetectDuplicateCollections(ModsRootPath, _config);

            DetectedDuplicateCollections.Clear();
            foreach (var group in duplicateCollections)
            {
                DetectedDuplicateCollections.Add(group);
            }

            // Show appropriate dialogs
            if (DetectedDuplicates.Count > 0)
            {
                IsShowingDuplicates = true;
            }

            if (DetectedDuplicateCollections.Count > 0)
            {
                IsShowingDuplicateCollections = true;
            }

            // Status message
            if (DetectedDuplicates.Count == 0 && DetectedDuplicateCollections.Count == 0)
            {
                StatusMessage = "No duplicate mods or collections found.";
            }
            else
            {
                var parts = new List<string>();
                if (DetectedDuplicates.Count > 0)
                    parts.Add($"{DetectedDuplicates.Count} duplicate mod(s)");
                if (DetectedDuplicateCollections.Count > 0)
                    parts.Add($"{DetectedDuplicateCollections.Count} duplicate collection(s)");
                StatusMessage = $"Found {string.Join(" and ", parts)}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to detect duplicates: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShareSelectedDuplicates()
    {
        var selected = DetectedDuplicates.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No mods selected to share.";
            return;
        }

        int sharedCount = 0;
        int skippedCount = 0;
        var skippedMods = new List<string>();

        foreach (var group in selected)
        {
            // Skip mods with version mismatches to avoid conflicts
            if (group.HasVersionMismatch)
            {
                skippedCount++;
                skippedMods.Add($"{group.ModName} (version mismatch)");
                continue;
            }

            try
            {
                // Get the profiles that have this mod
                var profilesWithMod = group.Instances.Select(i => i.ProfileName).ToList();

                // Find the mod entry from the first profile
                var firstProfile = profilesWithMod[0];
                var modEntry = FindModEntryByUniqueId(group.UniqueID, firstProfile);

                if (modEntry == null)
                {
                    skippedCount++;
                    skippedMods.Add($"{group.ModName} (source not found)");
                    continue;
                }

                // Share it across all profiles that have it
                _sharedModService.ShareMod(modEntry, profilesWithMod, ModsRootPath, _config);
                _configService.Save(_config);

                sharedCount++;
            }
            catch (Exception ex)
            {
                skippedCount++;
                skippedMods.Add($"{group.ModName} ({ex.Message})");
            }
        }

        // Refresh the mods for the current profile
        if (SelectedProfile != null)
            LoadModsForProfile(SelectedProfile.Name);

        // Show summary
        var summary = $"Shared {sharedCount} mod(s) across profiles.";
        if (skippedCount > 0)
        {
            summary += $"\n\nSkipped {skippedCount} mod(s):";
            foreach (var mod in skippedMods)
                summary += $"\n• {mod}";
        }

        StatusMessage = summary.Replace("\n", " ");
        IsShowingDuplicates = false;

        // Show detailed summary in a message box for user reference
        System.Windows.MessageBox.Show(summary, "Share Duplicates Complete",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private ModEntry? FindModEntryByUniqueId(string uniqueId, string profileName)
    {
        var profileDir = FileSystemHelpers.FindProfileDir(profileName, ModsRootPath);
        if (profileDir == null)
            return null;

        foreach (var dir in Directory.GetDirectories(profileDir))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var entry = FileSystemHelpers.ReadManifest(manifestPath);
            if (entry != null && entry.UniqueID.Equals(uniqueId, StringComparison.OrdinalIgnoreCase))
            {
                entry.FolderPath = dir;
                entry.FolderName = Path.GetFileName(dir);
                return entry;
            }
        }

        return null;
    }

    [RelayCommand]
    private void CloseDuplicates()
    {
        IsShowingDuplicates = false;
    }

    [RelayCommand]
    private void ShareSelectedDuplicateCollections()
    {
        var selected = DetectedDuplicateCollections.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No collections selected to share.";
            return;
        }

        int sharedCount = 0;
        int skippedCount = 0;
        var skippedCollections = new List<string>();

        foreach (var group in selected)
        {
            // Skip collections with identity mismatches to avoid conflicts
            if (group.HasIdentityMismatch)
            {
                skippedCount++;
                skippedCollections.Add($"{group.CollectionName} (content mismatch)");
                continue;
            }

            try
            {
                // Get the profiles that have this collection
                var profilesWithCollection = group.Instances.Select(i => i.ProfileName).ToList();

                // Find the collection entry from the first profile
                var firstProfile = profilesWithCollection[0];
                var collectionEntry = FindCollectionEntryByName(group.CollectionName, firstProfile);

                if (collectionEntry == null)
                {
                    skippedCount++;
                    skippedCollections.Add($"{group.CollectionName} (source not found)");
                    continue;
                }

                // Share it across all profiles that have it
                _sharedModService.ShareCollection(collectionEntry, profilesWithCollection, ModsRootPath, _config);
                _configService.Save(_config);

                sharedCount++;
            }
            catch (Exception ex)
            {
                skippedCount++;
                skippedCollections.Add($"{group.CollectionName} ({ex.Message})");
            }
        }

        // Refresh the mods for the current profile
        if (SelectedProfile != null)
            LoadModsForProfile(SelectedProfile.Name);

        // Show summary
        var summary = $"Shared {sharedCount} collection(s) across profiles.";
        if (skippedCount > 0)
        {
            summary += $"\n\nSkipped {skippedCount} collection(s):";
            foreach (var collection in skippedCollections)
                summary += $"\n• {collection}";
        }

        StatusMessage = summary.Replace("\n", " ");
        IsShowingDuplicateCollections = false;

        // Show detailed summary in a message box for user reference
        System.Windows.MessageBox.Show(summary, "Share Duplicate Collections Complete",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private ModEntry? FindCollectionEntryByName(string collectionName, string profileName)
    {
        var profileDir = FileSystemHelpers.FindProfileDir(profileName, ModsRootPath);
        if (profileDir == null)
            return null;

        var collectionPath = Path.Combine(profileDir, collectionName);
        if (!Directory.Exists(collectionPath))
        {
            // Try disabled version
            collectionPath = Path.Combine(profileDir, FolderNameHelper.ToDisabled(collectionName));
            if (!Directory.Exists(collectionPath))
                return null;
        }

        // Create a ModEntry for this collection
        var entry = new ModEntry
        {
            FolderPath = collectionPath,
            FolderName = Path.GetFileName(collectionPath),
            Name = collectionName,
            IsCollection = true,
            UniqueID = $"collection:{collectionName}"
        };

        return entry;
    }

    // ===== Shared Collections Commands =====

    public void OpenShareCollectionDialog(ModEntryViewModel collectionVm)
    {
        ModToShare = collectionVm;
        ShareTargetProfiles.Clear();

        foreach (var profileName in _config.ProfileNames)
        {
            var hasCollectionInProfile = false;
            int subModCount = 0;
            var actualDir = FileSystemHelpers.FindProfileDir(profileName, ModsRootPath);

            if (actualDir != null)
            {
                var cleanName = FolderNameHelper.GetCleanName(collectionVm.Model.FolderName);
                var collectionPath = Path.Combine(actualDir, cleanName);
                var collectionPathDisabled = Path.Combine(actualDir, FolderNameHelper.ToDisabled(cleanName));

                if (Directory.Exists(collectionPath) || Directory.Exists(collectionPathDisabled))
                {
                    hasCollectionInProfile = true;
                    var actualPath = Directory.Exists(collectionPath) ? collectionPath : collectionPathDisabled;
                    subModCount = Directory.GetDirectories(actualPath).Length;

                    try
                    {
                        var sourceFingerprint = FileSystemHelpers.CalculateCollectionFingerprint(collectionVm.Model.FolderPath);
                        var targetFingerprint = FileSystemHelpers.CalculateCollectionFingerprint(actualPath);
                        var hasIdentityMismatch = !FileSystemHelpers.CompareCollectionIdentities(sourceFingerprint, targetFingerprint, out _);

                        if (hasIdentityMismatch)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            if (hasCollectionInProfile)
            {
                ShareTargetProfiles.Add(new ShareTargetProfile
                {
                    ProfileName = profileName,
                    IsSelected = true,
                    CurrentVersion = $"{subModCount} mods",
                    HasVersionMismatch = false
                });
            }
        }

        IsShowingShareDialog = true;
    }

    public void UnshareCollection(ModEntryViewModel collectionVm)
    {
        if (collectionVm.SharedFolderName == null || SelectedProfile == null) return;

        try
        {
            _sharedModService.UnshareCollection(collectionVm.SharedFolderName, SelectedProfile.Name, ModsRootPath, _config);
            _configService.Save(_config);
            LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Unshared collection \"{collectionVm.Name}\" for this profile.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to unshare collection: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DetectDuplicateCollections()
    {
        try
        {
            var duplicates = await _sharedModService.DetectDuplicateCollections(ModsRootPath, _config);

            DetectedDuplicateCollections.Clear();
            foreach (var group in duplicates)
            {
                DetectedDuplicateCollections.Add(group);
            }

            if (DetectedDuplicateCollections.Count > 0)
            {
                IsShowingDuplicateCollections = true;
                StatusMessage = $"Found {DetectedDuplicateCollections.Count} duplicate collection(s).";
            }
            else
            {
                StatusMessage = "No duplicate collections found.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to detect duplicates: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseDuplicateCollections()
    {
        IsShowingDuplicateCollections = false;
    }
}
