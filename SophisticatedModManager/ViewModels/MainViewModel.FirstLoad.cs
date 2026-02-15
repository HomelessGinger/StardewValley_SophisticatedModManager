using System.IO;
using CommunityToolkit.Mvvm.Input;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: First-time detection and setup workflows.
/// </summary>
public partial class MainViewModel
{
    private void RunFirstLoadDetection()
    {
        var result = _detectionService.DetectExistingLayout(ModsRootPath, _config.SavesPath);
        _lastDetectionResult = result;
        IsFirstLoadActive = true;

        switch (result.Scenario)
        {
            case DetectionScenario.NoModsFolder:
                if (!Directory.Exists(ModsRootPath))
                    Directory.CreateDirectory(ModsRootPath);
                IsPromptingFirstProfile = true;
                FirstProfileName = string.Empty;
                break;

            case DetectionScenario.ModsExistNoProfiles:
                if (result.DotPrefixedFolders.Count > 0)
                {
                    ShowFolderVerification(result);
                }
                else
                {
                    IsAskingToOrganize = true;
                    OrganizeProfileName = string.Empty;
                }
                break;

            case DetectionScenario.ProfilesDetected:
                ShowFolderVerification(result);
                break;

            case DetectionScenario.ProfilesAndSaves:
                ImportDetectedProfiles(result.DetectedProfileNames);
                FinishFirstLoadDetection();
                break;
        }
    }

    private void ShowFolderVerification(DetectionResult result)
    {
        DetectedFolders.Clear();

        foreach (var folder in result.DotPrefixedFolders.Where(d => d.IsLikelyDisabledMod))
        {
            DetectedFolders.Add(CreateDetectedFolderItem(folder, FolderClassification.DisabledMod));
        }

        foreach (var folder in result.MultiModFolders)
        {
            DetectedFolders.Add(CreateDetectedFolderItem(folder, FolderClassification.Profile));
        }

        IsVerifyingFolders = true;
    }

    private DetectedFolderItem CreateDetectedFolderItem(DetectedFolder folder, FolderClassification classification)
    {
        return new DetectedFolderItem
        {
            FolderName = folder.FolderName,
            FullPath = folder.FullPath,
            IsLikelyProfile = classification == FolderClassification.Profile,
            IsLikelyDisabledMod = classification == FolderClassification.DisabledMod,
            SubModCount = folder.SubModCount,
            Classification = classification
        };
    }

    private void ShowSaveCategorization(DetectionResult result)
    {
        SavesToCategorize.Clear();
        foreach (var saveName in result.SaveFolderNames)
        {
            var item = new SaveCategoryItem { SaveFolderName = saveName };
            foreach (var profile in _config.ProfileNames)
                item.AvailableProfiles.Add(profile);
            SavesToCategorize.Add(item);
        }
        IsCategorizingSaves = true;
    }

    private void ImportDetectedProfiles(List<string> profileNames)
    {
        foreach (var name in profileNames)
        {
            if (!_config.ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                _config.ProfileNames.Add(name);
        }
        if (_config.ActiveProfileName == null && _config.ProfileNames.Count > 0)
            _config.ActiveProfileName = _config.ProfileNames[0];
        _configService.Save(_config);
    }

    private void DetectCollectionsInProfiles()
    {
        foreach (var profileName in _config.ProfileNames)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profileName, ModsRootPath);
            if (profileDir == null)
                continue;

            var collections = new List<string>();
            foreach (var subDir in Directory.GetDirectories(profileDir))
            {
                var folderName = Path.GetFileName(subDir);
                if (FolderNameHelper.IsDisabled(folderName)) continue;

                if (FileSystemHelpers.IsCollectionFolder(subDir))
                    collections.Add(folderName);
            }

            if (collections.Count > 0)
            {
                if (!_config.ProfileCollectionNames.ContainsKey(profileName))
                    _config.ProfileCollectionNames[profileName] = new List<string>();
                foreach (var col in collections)
                {
                    if (!_config.ProfileCollectionNames[profileName]
                            .Contains(col, StringComparer.OrdinalIgnoreCase))
                        _config.ProfileCollectionNames[profileName].Add(col);
                }
            }
        }
    }

    private void DetectCommonCollections()
    {
        if (!Directory.Exists(ModsRootPath)) return;

        foreach (var subDir in Directory.GetDirectories(ModsRootPath))
        {
            var folderName = Path.GetFileName(subDir);
            if (folderName.StartsWith(".")) continue;
            if (_config.ProfileNames.Any(n =>
                    string.Equals(folderName, ProfileService.ProfileModFolder(n), StringComparison.OrdinalIgnoreCase))) continue;

            if (FileSystemHelpers.IsCollectionFolder(subDir))
            {
                if (!_config.CommonCollectionNames.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                    _config.CommonCollectionNames.Add(folderName);
            }
        }
    }

    private void FinishFirstLoadDetection()
    {
        IsFirstLoadActive = false;
        IsVerifyingFolders = false;
        IsAskingToOrganize = false;
        IsCategorizingSaves = false;
        IsPromptingFirstProfile = false;

        CreateVanillaProfileIfMissing();

        DetectCollectionsInProfiles();
        DetectCommonCollections();

        if (_config.ProfileNames.Count > 0 && _config.ActiveProfileName != null)
        {
            try
            {
                _profileService.SwitchProfile(null, _config.ActiveProfileName, ModsRootPath, _config.SavesPath);

                if (IsVanillaProfile(_config.ActiveProfileName))
                {
                    _config.SavedCommonModEnabledFolders = GetEnabledCommonModFolders();
                    DisableAllCommonMods();
                }
            }
            catch { /* profile may already be active */ }
        }
        OnPropertyChanged(nameof(IsActiveProfileVanilla));
        _configService.Save(_config);
        LoadProfiles();
    }

    private void CreateVanillaProfileIfMissing()
    {
        const string vanillaName = "Vanilla";
        if (_config.ProfileNames.Contains(vanillaName, StringComparer.OrdinalIgnoreCase))
            return;

        try
        {
            _profileService.CreateProfile(vanillaName, ModsRootPath, _config.SavesPath);
            _config.ProfileNames.Insert(0, vanillaName);
            _config.VanillaProfileNames.Add(vanillaName);
        }
        catch { /* non-critical if creation fails */ }
    }

    [RelayCommand]
    private void ConfirmFirstProfile()
    {
        var error = FolderNameHelper.ValidateName(FirstProfileName, "Profile name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var name = FirstProfileName.Trim();

        try
        {
            _profileService.CreateProfile(name, ModsRootPath, _config.SavesPath);
            _config.ProfileNames.Add(name);
            _config.ActiveProfileName = name;
            IsPromptingFirstProfile = false;
            FinishFirstLoadDetection();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ConfirmOrganizeYes()
    {
        var error = FolderNameHelper.ValidateName(OrganizeProfileName, "Profile name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var name = OrganizeProfileName.Trim();

        try
        {
            var profileDir = Path.Combine(ModsRootPath, ProfileService.ProfileModFolder(name));
            Directory.CreateDirectory(profileDir);

            foreach (var dir in Directory.GetDirectories(ModsRootPath))
            {
                var folderName = Path.GetFileName(dir);
                if (folderName == ProfileService.ProfileModFolder(name)) continue;
                if (FolderNameHelper.IsDisabled(folderName)) continue;
                if (File.Exists(Path.Combine(dir, "manifest.json")))
                    Directory.Move(dir, Path.Combine(profileDir, folderName));
            }

            _config.ProfileNames.Add(name);
            _config.ActiveProfileName = name;
            IsAskingToOrganize = false;
            FinishFirstLoadDetection();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to organize mods: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ConfirmOrganizeNo()
    {
        var error = FolderNameHelper.ValidateName(OrganizeProfileName, "Profile name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var name = OrganizeProfileName.Trim();

        try
        {
            _profileService.CreateProfile(name, ModsRootPath, _config.SavesPath);
            _config.ProfileNames.Add(name);
            _config.ActiveProfileName = name;
            IsAskingToOrganize = false;
            FinishFirstLoadDetection();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ConfirmFolderVerification()
    {
        if (_isRefreshDetection)
        {
            var refreshProfiles = DetectedFolders
                .Where(d => d.Classification == FolderClassification.Profile)
                .Select(d => ProfileService.ParseProfileName(d.FolderName) ?? FolderNameHelper.GetCleanName(d.FolderName))
                .ToList();

            var refreshCollections = DetectedFolders
                .Where(d => d.Classification == FolderClassification.Collection)
                .Select(d => ProfileService.ParseProfileName(d.FolderName) ?? FolderNameHelper.GetCleanName(d.FolderName))
                .ToList();

            foreach (var name in refreshProfiles)
            {
                if (!_config.ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _config.ProfileNames.Add(name);
            }

            foreach (var name in refreshCollections)
            {
                if (!_config.CommonCollectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _config.CommonCollectionNames.Add(name);
            }

            _isRefreshDetection = false;
            OnPropertyChanged(nameof(IsRefreshDetection));
            IsVerifyingFolders = false;
            FinishRefresh();
            return;
        }

        var confirmedProfiles = DetectedFolders
            .Where(d => d.Classification == FolderClassification.Profile)
            .Select(d => ProfileService.ParseProfileName(d.FolderName) ?? FolderNameHelper.GetCleanName(d.FolderName))
            .ToList();

        var confirmedCollections = DetectedFolders
            .Where(d => d.Classification == FolderClassification.Collection)
            .Select(d => ProfileService.ParseProfileName(d.FolderName) ?? FolderNameHelper.GetCleanName(d.FolderName))
            .ToList();

        if (_lastDetectionResult != null)
        {
            foreach (var name in _lastDetectionResult.DetectedProfileNames)
            {
                var isReclassified = confirmedCollections.Contains(name, StringComparer.OrdinalIgnoreCase);
                if (!isReclassified && !confirmedProfiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                    confirmedProfiles.Add(name);
            }
        }

        IsVerifyingFolders = false;

        foreach (var name in confirmedCollections)
        {
            if (!_config.CommonCollectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                _config.CommonCollectionNames.Add(name);
        }

        if (confirmedProfiles.Count > 0)
        {
            ImportDetectedProfiles(confirmedProfiles);

            var result = _detectionService.DetectExistingLayout(ModsRootPath, _config.SavesPath);
            if (result.SaveFolderNames.Count > 0 && result.Scenario != DetectionScenario.ProfilesAndSaves)
                ShowSaveCategorization(result);
            else
                FinishFirstLoadDetection();
        }
        else
        {
            _configService.Save(_config);
            IsAskingToOrganize = true;
            OrganizeProfileName = string.Empty;
        }
    }

    [RelayCommand]
    private void ConfirmSaveCategorization()
    {
        if (_pendingDeleteProfileName != null)
        {
            var name = _pendingDeleteProfileName;
            try
            {
                string saveSourceDir;
                if (name == _config.ActiveProfileName)
                    saveSourceDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
                else
                    saveSourceDir = ProfileService.GetInactiveSavesPath(name, _config.SavesPath);

                MoveSavesToProfiles(saveSourceDir);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to reassign saves: {ex.Message}";
            }

            _pendingDeleteProfileName = null;
            IsCategorizingSaves = false;
            OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
            OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
            ExecuteProfileDeletion(name);
            return;
        }

        try
        {
            var savesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
            MoveSavesToProfiles(savesDir);

            if (Directory.Exists(savesDir) && Directory.GetDirectories(savesDir).Length == 0)
                Directory.Delete(savesDir);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to categorize saves: {ex.Message}";
        }

        IsCategorizingSaves = false;
        FinishFirstLoadDetection();
    }

    private void MoveSavesToProfiles(string savesDir)
    {
        foreach (var item in SavesToCategorize)
        {
            if (item.AssignedProfileName == null) continue;

            var saveSource = Path.Combine(savesDir, item.SaveFolderName);
            var targetDir = ProfileService.GetInactiveSavesPath(item.AssignedProfileName, _config.SavesPath);

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            if (Directory.Exists(saveSource))
            {
                var dest = Path.Combine(targetDir, item.SaveFolderName);
                if (!Directory.Exists(dest))
                    Directory.Move(saveSource, dest);
            }
        }
    }

    [RelayCommand]
    private void SkipSaveCategorization()
    {
        if (_pendingDeleteProfileName != null)
        {
            _pendingDeleteProfileName = null;
            IsCategorizingSaves = false;
            OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
            OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
            return;
        }

        IsCategorizingSaves = false;
        FinishFirstLoadDetection();
    }

    [RelayCommand]
    private void RefreshMods()
    {
        if (SelectedProfile != null)
            LoadModsForProfile(SelectedProfile.Name);
    }

    [RelayCommand]
    private void FullRefresh()
    {
        if (SelectedProfile == null) return;

        var result = _detectionService.DetectExistingLayout(ModsRootPath, _config.SavesPath);

        // Filter MultiModFolders to only unregistered ones
        var newFolders = new List<DetectedFolderItem>();
        foreach (var folder in result.MultiModFolders)
        {
            var parsed = ProfileService.ParseProfileName(folder.FolderName)
                         ?? FolderNameHelper.GetCleanName(folder.FolderName);

            if (_config.ProfileNames.Contains(parsed, StringComparer.OrdinalIgnoreCase))
                continue;

            if (_config.CommonCollectionNames.Contains(parsed, StringComparer.OrdinalIgnoreCase))
                continue;

            newFolders.Add(CreateDetectedFolderItem(folder, FolderClassification.Profile));
        }

        foreach (var folder in result.DotPrefixedFolders.Where(d => d.IsLikelyDisabledMod))
        {
            var parsed = FolderNameHelper.GetCleanName(folder.FolderName);
            if (newFolders.Any(f => f.FolderName == folder.FolderName))
                continue;
            if (_config.ProfileNames.Contains(parsed, StringComparer.OrdinalIgnoreCase))
                continue;

            newFolders.Add(CreateDetectedFolderItem(folder, FolderClassification.DisabledMod));
        }

        if (newFolders.Count > 0)
        {
            _isRefreshDetection = true;
            OnPropertyChanged(nameof(IsRefreshDetection));
            _lastDetectionResult = result;
            DetectedFolders.Clear();
            foreach (var f in newFolders)
                DetectedFolders.Add(f);
            IsVerifyingFolders = true;
        }
        else
        {
            FinishRefresh();
        }
    }

    private void FinishRefresh()
    {
        DetectCollectionsInProfiles();
        DetectCommonCollections();
        _configService.Save(_config);
        LoadProfiles();
        StatusMessage = "Mods refreshed.";
    }

    [RelayCommand]
    private void CancelRefreshDetection()
    {
        _isRefreshDetection = false;
        OnPropertyChanged(nameof(IsRefreshDetection));
        IsVerifyingFolders = false;
        FinishRefresh();
    }
}
