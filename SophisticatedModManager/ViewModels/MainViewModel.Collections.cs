using System.IO;
using CommunityToolkit.Mvvm.Input;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Collection management (create, convert to/from profiles).
/// </summary>
public partial class MainViewModel
{
    private void CreateCollection(string location)
    {
        NewCollectionName = string.Empty;
        CreateCollectionIsCommon = location == "Common";
        IsCreatingCollection = true;
    }

    [RelayCommand]
    private void ConfirmCreateCollection()
    {
        var error = FolderNameHelper.ValidateName(NewCollectionName, "Collection name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var name = NewCollectionName.Trim();

        try
        {
            string targetDir;
            if (CreateCollectionIsCommon)
            {
                targetDir = Path.Combine(ModsRootPath, name);
                if (!_config.CommonCollectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _config.CommonCollectionNames.Add(name);
            }
            else
            {
                if (SelectedProfile == null)
                {
                    StatusMessage = "Select a profile first.";
                    return;
                }
                targetDir = Path.Combine(ModsRootPath, ProfileService.ProfileModFolder(SelectedProfile.Name), name);
                if (!_config.ProfileCollectionNames.ContainsKey(SelectedProfile.Name))
                    _config.ProfileCollectionNames[SelectedProfile.Name] = new List<string>();
                var list = _config.ProfileCollectionNames[SelectedProfile.Name];
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                    list.Add(name);
            }

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            _configService.Save(_config);
            IsCreatingCollection = false;
            NewCollectionName = string.Empty;

            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Collection \"{name}\" created.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create collection: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelCreateCollection()
    {
        IsCreatingCollection = false;
        NewCollectionName = string.Empty;
    }

    [RelayCommand]
    private void ConvertCollectionToProfile(ModEntryViewModel collectionVm)
    {
        _collectionToConvert = collectionVm;
        ConvertCollectionProfileName = collectionVm.Name;
        IsConvertingCollectionToProfile = true;
    }

    [RelayCommand]
    private void ConfirmConvertCollectionToProfile()
    {
        if (_collectionToConvert == null) return;

        var error = FolderNameHelper.ValidateName(ConvertCollectionProfileName, "Profile name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var name = ConvertCollectionProfileName.Trim();

        if (_config.ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "A profile with that name already exists.";
            return;
        }

        try
        {
            var collectionModel = _collectionToConvert.Model;
            var collectionName = FolderNameHelper.GetCleanName(collectionModel.FolderName);
            var collectionPath = collectionModel.FolderPath;

            // Create new profile (inactive) — creates .[PROFILE] {name} folder + saves dir
            _profileService.CreateProfile(name, ModsRootPath, _config.SavesPath);

            // Determine the new profile mod folder path (inactive)
            var profileDir = ProfileService.GetInactiveProfileModPath(name, ModsRootPath);

            // Move all sub-mod folders from collection into profile folder
            foreach (var subDir in Directory.GetDirectories(collectionPath))
            {
                var subFolderName = Path.GetFileName(subDir);
                var destPath = Path.Combine(profileDir, subFolderName);
                Directory.Move(subDir, destPath);
            }

            // Delete the now-empty collection folder
            if (Directory.Exists(collectionPath))
                Directory.Delete(collectionPath, false);

            // Remove collection from config
            if (collectionModel.IsCommon)
            {
                _config.CommonCollectionNames.Remove(collectionName);
            }
            else if (SelectedProfile != null &&
                     _config.ProfileCollectionNames.TryGetValue(SelectedProfile.Name, out var list))
            {
                list.Remove(collectionName);
            }

            // Register the new profile
            _config.ProfileNames.Add(name);

            _configService.Save(_config);
            IsConvertingCollectionToProfile = false;
            _collectionToConvert = null;
            ConvertCollectionProfileName = string.Empty;
            LoadProfiles();

            StatusMessage = $"Converted collection \"{collectionName}\" to profile \"{name}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to convert collection to profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelConvertCollectionToProfile()
    {
        IsConvertingCollectionToProfile = false;
        _collectionToConvert = null;
        ConvertCollectionProfileName = string.Empty;
    }

    [RelayCommand]
    private void ConvertProfileToCollection()
    {
        if (SelectedProfile == null) return;

        if (_config.ProfileNames.Count <= 1)
        {
            StatusMessage = "Cannot convert the only remaining profile to a collection.";
            return;
        }

        _profileToConvert = SelectedProfile.Name;
        ConvertProfileCollectionName = SelectedProfile.Name;
        ConvertProfileToCommon = true;
        ConvertProfileTargetProfileName = null;
        IsConvertingProfileToCollection = true;
    }

    [RelayCommand]
    private void ConfirmConvertProfileToCollection()
    {
        if (_profileToConvert == null) return;

        var profileName = _profileToConvert;
        var error = FolderNameHelper.ValidateName(ConvertProfileCollectionName, "Collection name");
        if (error != null)
        {
            StatusMessage = error;
            return;
        }
        var collectionName = ConvertProfileCollectionName.Trim();

        // Check for name collision at target
        if (ConvertProfileToCommon)
        {
            if (_config.CommonCollectionNames.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
            {
                StatusMessage = "A common collection with that name already exists.";
                return;
            }
            var existingDir = Path.Combine(ModsRootPath, collectionName);
            if (Directory.Exists(existingDir))
            {
                StatusMessage = "A folder with that name already exists in Mods root.";
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ConvertProfileTargetProfileName))
            {
                StatusMessage = "Select a target profile for the collection.";
                return;
            }
            if (string.Equals(ConvertProfileTargetProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Cannot place the collection inside the profile being converted.";
                return;
            }
            var targetList = _config.ProfileCollectionNames.GetValueOrDefault(
                ConvertProfileTargetProfileName!, new List<string>());
            if (targetList.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
            {
                StatusMessage = "A collection with that name already exists in the target profile.";
                return;
            }
        }

        // Warn about saves
        if (ProfileHasSaves(profileName))
        {
            var result = System.Windows.MessageBox.Show(
                $"Profile \"{profileName}\" has save data. Converting to a collection will delete those saves.\n\nContinue?",
                "Save Data Warning",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) return;
        }

        try
        {
            bool wasActive = (profileName == _config.ActiveProfileName);
            var wasVanilla = IsVanillaProfile(profileName);

            // If active, deactivate first
            if (wasActive)
            {
                var savesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
                var hiddenSaves = ProfileService.GetInactiveSavesPath(profileName, _config.SavesPath);
                if (Directory.Exists(savesDir) && !Directory.Exists(hiddenSaves))
                    Directory.Move(savesDir, hiddenSaves);

                var modDir = ProfileService.GetActiveProfileModPath(profileName, ModsRootPath);
                var hiddenMod = ProfileService.GetInactiveProfileModPath(profileName, ModsRootPath);
                if (Directory.Exists(modDir) && !Directory.Exists(hiddenMod))
                    Directory.Move(modDir, hiddenMod);

                _config.ActiveProfileName = null;
            }

            // Restore common mods if was vanilla
            if (wasVanilla && wasActive)
            {
                RestoreCommonMods(_config.SavedCommonModEnabledFolders);
                _config.SavedCommonModEnabledFolders.Clear();
            }

            // Locate the profile mod folder (now inactive)
            var profileModDir = ProfileService.GetInactiveProfileModPath(profileName, ModsRootPath);
            if (!Directory.Exists(profileModDir))
                profileModDir = ProfileService.GetActiveProfileModPath(profileName, ModsRootPath);

            // Flatten sub-collections: move sub-mods up one level
            var profileCollections = _config.ProfileCollectionNames
                .GetValueOrDefault(profileName, new List<string>());
            foreach (var subCollName in profileCollections.ToList())
            {
                var subCollDir = Path.Combine(profileModDir, subCollName);
                var subCollDirDisabled = Path.Combine(profileModDir, FolderNameHelper.ToDisabled(subCollName));
                var actualSubCollDir = Directory.Exists(subCollDir) ? subCollDir :
                    Directory.Exists(subCollDirDisabled) ? subCollDirDisabled : null;
                if (actualSubCollDir != null)
                {
                    foreach (var item in Directory.GetDirectories(actualSubCollDir))
                    {
                        var itemName = Path.GetFileName(item);
                        var destPath = Path.Combine(profileModDir, itemName);
                        if (!Directory.Exists(destPath))
                            Directory.Move(item, destPath);
                    }
                    if (Directory.Exists(actualSubCollDir))
                        Directory.Delete(actualSubCollDir, true);
                }
            }

            // Determine collection target directory
            string collectionDir;
            if (ConvertProfileToCommon)
            {
                collectionDir = Path.Combine(ModsRootPath, collectionName);
            }
            else
            {
                var targetProfileDir = FileSystemHelpers.FindProfileDir(ConvertProfileTargetProfileName!, ModsRootPath);
                if (targetProfileDir == null)
                    throw new InvalidOperationException($"Target profile directory not found for {ConvertProfileTargetProfileName}");
                collectionDir = Path.Combine(targetProfileDir, collectionName);
            }

            // Create collection folder and move all contents
            Directory.CreateDirectory(collectionDir);

            if (Directory.Exists(profileModDir))
            {
                foreach (var subDir in Directory.GetDirectories(profileModDir))
                {
                    var subFolderName = Path.GetFileName(subDir);
                    var destPath = Path.Combine(collectionDir, subFolderName);
                    Directory.Move(subDir, destPath);
                }
                foreach (var file in Directory.GetFiles(profileModDir))
                {
                    var destFile = Path.Combine(collectionDir, Path.GetFileName(file));
                    File.Move(file, destFile);
                }
            }

            // Register collection in config
            if (ConvertProfileToCommon)
            {
                if (!_config.CommonCollectionNames.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                    _config.CommonCollectionNames.Add(collectionName);
            }
            else
            {
                var targetProfile = ConvertProfileTargetProfileName!;
                if (!_config.ProfileCollectionNames.ContainsKey(targetProfile))
                    _config.ProfileCollectionNames[targetProfile] = new List<string>();
                var targetList = _config.ProfileCollectionNames[targetProfile];
                if (!targetList.Contains(collectionName, StringComparer.OrdinalIgnoreCase))
                    targetList.Add(collectionName);
            }

            // Cleanup shared mods
            _sharedModService.CleanupProfileSharedMods(profileName, ModsRootPath, _config);

            // Delete the now-empty profile folder
            if (Directory.Exists(profileModDir))
                Directory.Delete(profileModDir, true);

            // Delete saves
            var savesHidden = ProfileService.GetInactiveSavesPath(profileName, _config.SavesPath);
            if (Directory.Exists(savesHidden))
                Directory.Delete(savesHidden, true);

            // Remove profile from config
            _config.ProfileNames.Remove(profileName);
            _config.ProfileCollectionNames.Remove(profileName);
            _config.VanillaProfileNames.RemoveAll(n =>
                string.Equals(n, profileName, StringComparison.OrdinalIgnoreCase));

            // Activate next available profile if needed
            if (_config.ProfileNames.Count > 0 && _config.ActiveProfileName == null)
            {
                var newActive = _config.ProfileNames[0];
                _profileService.SwitchProfile(null, newActive, ModsRootPath, _config.SavesPath);
                _config.ActiveProfileName = newActive;
            }

            _configService.Save(_config);
            IsConvertingProfileToCollection = false;
            _profileToConvert = null;
            ConvertProfileCollectionName = string.Empty;
            LoadProfiles();

            StatusMessage = $"Converted profile \"{profileName}\" to collection \"{collectionName}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to convert profile to collection: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelConvertProfileToCollection()
    {
        IsConvertingProfileToCollection = false;
        _profileToConvert = null;
        ConvertProfileCollectionName = string.Empty;
    }
}
