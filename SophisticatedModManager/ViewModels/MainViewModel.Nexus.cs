using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.Input;
using SharpCompress.Archives;
using SharpCompress.Common;
using SophisticatedModManager.Models;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Nexus Mods integration (updates, downloads, endorsements, NXM protocol).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private async Task ValidateNexusApiKey()
    {
        if (string.IsNullOrWhiteSpace(SettingsNexusApiKey))
        {
            NexusApiKeyStatus = "Please enter an API key.";
            return;
        }

        NexusApiKeyStatus = "Validating...";
        var result = await _nexusService.ValidateApiKeyAsync(SettingsNexusApiKey.Trim());
        if (result != null)
        {
            var tier = result.IsPremium ? "Premium" : "Free";
            NexusApiKeyStatus = $"Valid — {result.Name} ({tier})";
        }
        else
        {
            NexusApiKeyStatus = "Invalid API key.";
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (!_nexusService.IsConfigured) return;

        IsCheckingUpdates = true;
        UpdatesAvailableCount = 0;
        StatusMessage = "Checking for updates...";

        var allMods = ProfileMods.Concat(CommonMods).ToList();
        int updatesFound = 0;

        foreach (var mod in allMods)
        {
            if (mod.NexusModId == null) continue;

            mod.IsCheckingUpdate = true;
            try
            {
                var nexusInfo = await _nexusService.GetModInfoAsync(mod.NexusModId.Value);
                if (nexusInfo != null && VersionComparer.IsNewer(nexusInfo.Version, mod.Version))
                {
                    mod.HasUpdate = true;
                    mod.LatestVersion = nexusInfo.Version;
                    updatesFound++;
                }
                else
                {
                    mod.HasUpdate = false;
                    mod.LatestVersion = string.Empty;
                }
            }
            catch
            {
            }
            finally
            {
                mod.IsCheckingUpdate = false;
            }
        }

        UpdatesAvailableCount = updatesFound;
        IsCheckingUpdates = false;
        StatusMessage = updatesFound > 0
            ? $"{updatesFound} update(s) available."
            : "All mods are up to date.";
    }

    [RelayCommand]
    private async Task UpdateMod(ModEntryViewModel mod)
    {
        if (mod.NexusModId == null) return;

        if (mod.IsShared)
        {
            SharedModUpdatePendingMod = mod;
            IsPromptingSharedModUpdate = true;
            return;
        }

        mod.IsUpdating = true;
        try
        {
            await DownloadAndInstallFromNexus(mod.NexusModId.Value, mod.Model.FolderPath);
            mod.HasUpdate = false;
            mod.IsUpdating = false;
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Updated \"{mod.Name}\" successfully.";
        }
        catch (Exception ex)
        {
            mod.IsUpdating = false;
            StatusMessage = $"Failed to update \"{mod.Name}\": {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateAllMods()
    {
        var modsToUpdate = ProfileMods.Concat(CommonMods).Where(m => m.HasUpdate).ToList();
        if (modsToUpdate.Count == 0) return;

        foreach (var mod in modsToUpdate)
            await UpdateMod(mod);
    }

    [RelayCommand]
    private async Task ViewChangelog(ModEntryViewModel? mod)
    {
        if (mod?.NexusModId == null) return;

        ChangelogModName = mod.Name;
        ChangelogText = string.Empty;
        IsLoadingChangelog = true;
        IsViewingChangelog = true;

        try
        {
            var changelog = await _nexusService.GetChangelogAsync(mod.NexusModId.Value);

            if (changelog.Count == 0)
            {
                ChangelogText = "No changelog available for this mod.";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (version, changes) in changelog.OrderByDescending(kv => kv.Key))
                {
                    sb.AppendLine($"v{version}");
                    foreach (var change in changes)
                        sb.AppendLine($"  - {change}");
                    sb.AppendLine();
                }
                ChangelogText = sb.ToString().TrimEnd();
            }
        }
        catch
        {
            ChangelogText = "Failed to load changelog.";
        }
        finally
        {
            IsLoadingChangelog = false;
        }
    }

    [RelayCommand]
    private void CloseChangelog()
    {
        IsViewingChangelog = false;
        ChangelogModName = string.Empty;
        ChangelogText = string.Empty;
    }

    [RelayCommand]
    private async Task ToggleEndorseMod(ModEntryViewModel? mod)
    {
        if (mod?.NexusModId == null || !_nexusService.IsConfigured) return;

        mod.IsEndorsing = true;
        try
        {
            bool success;
            if (mod.IsEndorsed)
            {
                success = await _nexusService.AbstainModAsync(mod.NexusModId.Value, mod.Version);
                if (success)
                {
                    mod.IsEndorsed = false;
                    _endorsedModIds.Remove(mod.NexusModId.Value);
                    StatusMessage = $"Removed endorsement for \"{mod.Name}\".";
                }
            }
            else
            {
                success = await _nexusService.EndorseModAsync(mod.NexusModId.Value, mod.Version);
                if (success)
                {
                    mod.IsEndorsed = true;
                    _endorsedModIds.Add(mod.NexusModId.Value);
                    StatusMessage = $"Endorsed \"{mod.Name}\" on Nexus Mods!";
                }
            }

            if (!success)
                StatusMessage = $"Failed to update endorsement for \"{mod.Name}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Endorsement failed: {ex.Message}";
        }
        finally
        {
            mod.IsEndorsing = false;
        }
    }

    [RelayCommand]
    private async Task BrowseNexus()
    {
        IsNexusBrowsing = true;
        NexusBrowseCategory = "Trending";
        await LoadNexusBrowseCategory("Trending");
    }

    [RelayCommand]
    private void CloseBrowseNexus()
    {
        IsNexusBrowsing = false;
        NexusBrowseResults.Clear();
        _recentlyUpdatedModIds.Clear();
        _recentlyUpdatedLoadIndex = 0;
        CanLoadMoreNexus = false;
    }

    [RelayCommand]
    private async Task SwitchNexusBrowseCategory(string category)
    {
        NexusBrowseCategory = category;
        await LoadNexusBrowseCategory(category);
    }

    [RelayCommand]
    private async Task ViewModDetail(NexusBrowseItem item)
    {
        ModDetailItem = item;
        ModDetailDescription = item.Summary;
        ModDetailFiles.Clear();
        IsViewingModDetail = true;
        IsLoadingModDetail = true;

        try
        {
            var info = await _nexusService.GetModInfoAsync(item.ModId);
            if (info != null)
                ModDetailDescription = info.Summary;

            var files = await _nexusService.GetModFilesAsync(item.ModId);
            foreach (var file in files.OrderByDescending(f => f.FileId))
                ModDetailFiles.Add(file);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load mod details: {ex.Message}";
        }

        IsLoadingModDetail = false;
    }

    [RelayCommand]
    private void CloseModDetail()
    {
        IsViewingModDetail = false;
        ModDetailItem = null;
        ModDetailDescription = string.Empty;
        ModDetailFiles.Clear();
    }

    [RelayCommand]
    private void OpenModInBrowser(NexusBrowseItem item)
    {
        OpenNexusModPage(item.ModId);
    }

    [RelayCommand]
    private async Task InstallModFile(NexusModFile file)
    {
        if (ModDetailItem == null) return;

        var targetDir = await ResolveInstallTarget();
        if (targetDir == null) return;

        StatusMessage = $"Installing \"{file.Name}\"...";

        try
        {
            var downloadUrl = await _nexusService.GetDownloadLinkAsync(ModDetailItem.ModId, file.FileId);
            if (downloadUrl == null)
            {
                OpenNexusModPage(ModDetailItem.ModId);
                StatusMessage = "Download requires Nexus Premium or use 'Download with Manager' on the Nexus website.";
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "SMM_NexusDL_" + Guid.NewGuid().ToString("N")[..8]);
            var downloadedFile = await _nexusService.DownloadFileAsync(downloadUrl, tempDir);

            var extractDir = FileSystemHelpers.ExtractArchive(downloadedFile);
            var modFolders = FindModFolders(extractDir);
            if (modFolders.Count == 0)
            {
                StatusMessage = "No mod folders found in the downloaded file.";
                CleanupTempDir(tempDir);
                CleanupTempDir(extractDir);
                return;
            }

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            BackupSavesBeforeModInstall(targetDir);
            FileSystemHelpers.CopyModFolders(modFolders, targetDir);
            CleanupTempDir(tempDir);
            CleanupTempDir(extractDir);

            StatusMessage = $"Installed \"{file.Name}\" successfully.";
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to install: {ex.Message}";
        }
    }

    private async Task LoadNexusBrowseCategory(string category)
    {
        IsNexusBrowseLoading = true;
        NexusBrowseResults.Clear();
        _recentlyUpdatedModIds.Clear();
        _recentlyUpdatedLoadIndex = 0;

        try
        {
            if (category == "Recently Updated")
            {
                _recentlyUpdatedModIds = await _nexusService.GetRecentlyUpdatedModIdsAsync();
                await HydrateNextBatch();
            }
            else
            {
                var mods = category switch
                {
                    "Trending" => await _nexusService.GetTrendingModsAsync(),
                    "New" => await _nexusService.GetLatestAddedModsAsync(),
                    _ => new List<NexusModInfo>()
                };

                foreach (var mod in mods)
                {
                    NexusBrowseResults.Add(new NexusBrowseItem
                    {
                        ModId = mod.ModId,
                        Name = mod.Name,
                        Summary = mod.Summary,
                        Author = mod.Author,
                        Version = mod.Version,
                        PictureUrl = mod.PictureUrl
                    });
                }

                CanLoadMoreNexus = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load Nexus mods: {ex.Message}";
        }

        IsNexusBrowseLoading = false;
    }

    private async Task HydrateNextBatch()
    {
        var batch = _recentlyUpdatedModIds
            .Skip(_recentlyUpdatedLoadIndex)
            .Take(BrowseBatchSize)
            .ToList();

        foreach (var modId in batch)
        {
            try
            {
                var mod = await _nexusService.GetModInfoAsync(modId);
                if (mod != null)
                {
                    NexusBrowseResults.Add(new NexusBrowseItem
                    {
                        ModId = mod.ModId,
                        Name = mod.Name,
                        Summary = mod.Summary,
                        Author = mod.Author,
                        Version = mod.Version,
                        PictureUrl = mod.PictureUrl
                    });
                }
            }
            catch
            {
            }
        }

        _recentlyUpdatedLoadIndex += batch.Count;
        CanLoadMoreNexus = _recentlyUpdatedLoadIndex < _recentlyUpdatedModIds.Count;
    }

    [RelayCommand]
    private async Task LoadMoreNexusMods()
    {
        IsNexusBrowseLoading = true;
        try
        {
            await HydrateNextBatch();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load more mods: {ex.Message}";
        }
        IsNexusBrowseLoading = false;
    }

    [RelayCommand]
    private async Task InstallFromNexusUrl()
    {
        if (string.IsNullOrWhiteSpace(NexusInstallUrl)) return;

        var modId = ParseModIdFromInput(NexusInstallUrl.Trim());
        if (modId == null)
        {
            NexusInstallStatus = "Invalid Nexus URL or mod ID.";
            return;
        }

        var targetDir = await ResolveInstallTarget();
        if (targetDir == null) return;

        IsInstallingFromNexus = true;
        NexusInstallStatus = "Fetching mod info...";

        try
        {
            var info = await _nexusService.GetModInfoAsync(modId.Value);
            if (info == null)
            {
                NexusInstallStatus = "Mod not found.";
                IsInstallingFromNexus = false;
                return;
            }

            NexusInstallStatus = $"Installing \"{info.Name}\"...";
            await DownloadAndInstallFromNexus(modId.Value, null, targetDir);

            NexusInstallStatus = $"Installed \"{info.Name}\" successfully.";
            NexusInstallUrl = string.Empty;

            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
        catch (Exception ex)
        {
            NexusInstallStatus = $"Failed: {ex.Message}";
        }

        IsInstallingFromNexus = false;
    }

    [RelayCommand]
    private async Task InstallNexusBrowseItem(NexusBrowseItem item)
    {
        if (item.IsInstalling) return;

        var targetDir = await ResolveInstallTarget();
        if (targetDir == null) return;

        item.IsInstalling = true;

        try
        {
            await DownloadAndInstallFromNexus(item.ModId, null, targetDir);
            StatusMessage = $"Installed \"{item.Name}\" successfully.";

            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to install \"{item.Name}\": {ex.Message}";
        }

        item.IsInstalling = false;
    }

    private async Task DownloadAndInstallFromNexus(int modId, string? existingModPath, string? targetDir = null)
    {
        var files = await _nexusService.GetModFilesAsync(modId);
        var mainFile = files
            .Where(f => f.CategoryName is "MAIN" or "UPDATE")
            .FirstOrDefault(f => f.IsPrimary) ?? files
            .Where(f => f.CategoryName == "MAIN")
            .MaxBy(f => f.FileId);

        if (mainFile == null)
        {
            OpenNexusModPage(modId);
            throw new InvalidOperationException("No downloadable main file found. Opening Nexus page.");
        }

        var downloadUrl = await _nexusService.GetDownloadLinkAsync(modId, mainFile.FileId);

        if (downloadUrl == null)
        {
            OpenNexusModPage(modId);
            throw new InvalidOperationException("Download requires Nexus Premium or use 'Download with Manager' on the Nexus website.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "SMM_NexusDL_" + Guid.NewGuid().ToString("N")[..8]);
        var downloadedFile = await _nexusService.DownloadFileAsync(downloadUrl, tempDir);

        try
        {
            var extractDir = FileSystemHelpers.ExtractArchive(downloadedFile);
            var modFolders = FindModFolders(extractDir);

            if (modFolders.Count == 0)
            {
                CleanupTempDir(tempDir);
                CleanupTempDir(extractDir);
                throw new InvalidOperationException("No mod folders found in the downloaded archive.");
            }

            if (existingModPath != null && Directory.Exists(existingModPath))
            {
                var parentDir = Path.GetDirectoryName(existingModPath)!;
                var folderName = Path.GetFileName(existingModPath);

                BackupSavesBeforeModInstall(parentDir);

                Directory.Delete(existingModPath, true);

                FileSystemHelpers.CopyDirectory(modFolders[0], Path.Combine(parentDir, folderName));
            }
            else if (targetDir != null)
            {
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                BackupSavesBeforeModInstall(targetDir);
                FileSystemHelpers.CopyModFolders(modFolders, targetDir);
            }

            CleanupTempDir(tempDir);
            CleanupTempDir(extractDir);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupTempDir(tempDir);
            throw new InvalidOperationException($"Failed to extract/install: {ex.Message}", ex);
        }
    }

    public async Task HandleNxmUrl(string nxmUrl)
    {
        var nxmInfo = _nexusService.ParseNxmUrl(nxmUrl);
        if (nxmInfo == null)
        {
            StatusMessage = "Invalid NXM link.";
            return;
        }

        if (!nxmInfo.GameDomain.Equals("stardewvalley", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "This NXM link is not for Stardew Valley.";
            return;
        }

        var targetDir = await ResolveInstallTarget();
        if (targetDir == null) return;

        StatusMessage = $"Downloading from Nexus (mod {nxmInfo.ModId})...";

        try
        {
            var downloadUrl = await _nexusService.GetDownloadLinkAsync(
                nxmInfo.ModId, nxmInfo.FileId, nxmInfo.Key, nxmInfo.Expires);

            if (downloadUrl == null)
            {
                StatusMessage = "Failed to get download link from Nexus.";
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "SMM_NexusDL_" + Guid.NewGuid().ToString("N")[..8]);
            var downloadedFile = await _nexusService.DownloadFileAsync(downloadUrl, tempDir);

            var extractDir = FileSystemHelpers.ExtractArchive(downloadedFile);

            // Check for collection: single top-level folder with multiple sub-mods
            var topDirs = Directory.GetDirectories(extractDir);

            if (topDirs.Length == 1 && !File.Exists(Path.Combine(topDirs[0], "manifest.json")))
            {
                var subModFolders = FindModFolders(topDirs[0]);
                if (subModFolders.Count >= 2)
                {
                    if (!Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);
                    BackupSavesBeforeModInstall(targetDir);

                    var collectionName = Path.GetFileName(topDirs[0]);
                    var destDir = Path.Combine(targetDir, collectionName);
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, true);
                    FileSystemHelpers.CopyDirectory(topDirs[0], destDir);
                    RegisterCollection(collectionName, targetDir);

                    CleanupTempDir(tempDir);
                    CleanupTempDir(extractDir);
                    StatusMessage = $"Installed \"{collectionName}\" collection ({subModFolders.Count} mods) from Nexus.";
                    FullRefresh();
                    return;
                }
            }

            // Fallback: individual mod folders
            var modFolders = FindModFolders(extractDir);
            if (modFolders.Count == 0)
            {
                StatusMessage = "No mod folders found in the downloaded archive.";
                CleanupTempDir(tempDir);
                CleanupTempDir(extractDir);
                return;
            }

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            BackupSavesBeforeModInstall(targetDir);
            FileSystemHelpers.CopyModFolders(modFolders, targetDir);

            CleanupTempDir(tempDir);
            CleanupTempDir(extractDir);

            StatusMessage = $"Installed {modFolders.Count} mod(s) from Nexus.";
            FullRefresh();
        }
        catch (Exception ex)
        {
            StatusMessage = $"NXM install failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ModsRootPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SophisticatedModManager");
        Process.Start(new ProcessStartInfo
        {
            FileName = configDir,
            UseShellExecute = true
        });
    }

    private static void OpenNexusModPage(int modId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://www.nexusmods.com/stardewvalley/mods/{modId}?tab=files",
            UseShellExecute = true
        });
    }

    private static int? ParseModIdFromInput(string input)
    {
        if (int.TryParse(input, out var directId))
            return directId;

        try
        {
            var uri = new Uri(input);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] == "mods" && int.TryParse(segments[i + 1], out var id))
                    return id;
            }
        }
        catch
        {
        }

        return null;
    }


    private async Task<string?> ResolveInstallTarget()
    {
        if (NexusInstallToActiveProfile && SelectedProfile != null)
            return Path.Combine(ModsRootPath, ProfileService.ProfileModFolder(SelectedProfile.Name));

        InstallTargetOptions.Clear();
        InstallTargetOptions.Add(new InstallTargetOption { Label = "Common Mods", TargetDir = ModsRootPath });
        foreach (var profile in _config.ProfileNames)
        {
            var dir = Path.Combine(ModsRootPath, ProfileService.ProfileModFolder(profile));
            InstallTargetOptions.Add(new InstallTargetOption { Label = profile, TargetDir = dir });
        }

        SelectedInstallTarget = SelectedProfile != null
            ? InstallTargetOptions.FirstOrDefault(o => o.Label == SelectedProfile.Name)
            : InstallTargetOptions.FirstOrDefault();

        _installTargetTcs = new TaskCompletionSource<string?>();
        IsChoosingInstallTarget = true;

        var result = await _installTargetTcs.Task;
        IsChoosingInstallTarget = false;
        return result;
    }

    [RelayCommand]
    private void ConfirmInstallTarget()
    {
        _installTargetTcs?.TrySetResult(SelectedInstallTarget?.TargetDir);
    }

    [RelayCommand]
    private void CancelInstallTarget()
    {
        _installTargetTcs?.TrySetResult(null);
    }
}
