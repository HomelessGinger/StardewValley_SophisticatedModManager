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
            StatusMessage = "Select at least 2 profiles to share a mod.";
            return;
        }

        try
        {
            _sharedModService.ShareMod(ModToShare.Model, selectedProfiles, ModsRootPath, _config);
            _configService.Save(_config);
            IsShowingShareDialog = false;
            ModToShare = null;
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = "Mod shared successfully across profiles.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to share mod: {ex.Message}";
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
    private void DetectDuplicates()
    {
        try
        {
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

            IsShowingDuplicates = true;

            if (DetectedDuplicates.Count == 0)
                StatusMessage = "No duplicate mods found.";
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
}
