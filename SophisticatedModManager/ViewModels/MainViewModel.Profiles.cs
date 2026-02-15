using System.IO;
using CommunityToolkit.Mvvm.Input;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Profile management (create, delete, rename, switch).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private void CreateProfile()
    {
        NewProfileName = string.Empty;
        IsCreatingProfile = true;
    }

    [RelayCommand]
    private void ConfirmCreateProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Profile name cannot be empty.";
            return;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
        {
            StatusMessage = "Profile name contains invalid characters.";
            return;
        }

        if (_config.ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "A profile with that name already exists.";
            return;
        }

        try
        {
            _profileService.CreateProfile(name, ModsRootPath, _config.SavesPath);
            _config.ProfileNames.Add(name);

            if (_config.ProfileNames.Count == 1)
            {
                _profileService.SwitchProfile(null, name, ModsRootPath, _config.SavesPath);
                _config.ActiveProfileName = name;
            }

            _configService.Save(_config);
            IsCreatingProfile = false;
            NewProfileName = string.Empty;
            LoadProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelCreateProfile()
    {
        IsCreatingProfile = false;
        NewProfileName = string.Empty;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void ConfirmDeleteProfile()
    {
        if (SelectedProfile == null) return;

        var name = SelectedProfile.Name;

        if (ProfileHasSaves(name))
        {
            _pendingDeleteProfileName = name;
            IsConfirmingDelete = false;
            IsPromptingDeleteSaves = true;
            OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
            OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
            return;
        }

        ExecuteProfileDeletion(name);
    }

    private bool ProfileHasSaves(string name)
    {
        var activeSavesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
        var hiddenSavesDir = ProfileService.GetInactiveSavesPath(name, _config.SavesPath);

        if (name == _config.ActiveProfileName &&
            Directory.Exists(activeSavesDir) &&
            Directory.GetDirectories(activeSavesDir).Length > 0)
            return true;

        if (Directory.Exists(hiddenSavesDir) &&
            Directory.GetDirectories(hiddenSavesDir).Length > 0)
            return true;

        return false;
    }

    private void ExecuteProfileDeletion(string name)
    {
        try
        {
            if (name == _config.ActiveProfileName)
            {
                var savesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
                var hiddenSaves = ProfileService.GetInactiveSavesPath(name, _config.SavesPath);
                if (Directory.Exists(savesDir) && !Directory.Exists(hiddenSaves))
                    Directory.Move(savesDir, hiddenSaves);

                var modDir = ProfileService.GetActiveProfileModPath(name, ModsRootPath);
                var hiddenMod = ProfileService.GetInactiveProfileModPath(name, ModsRootPath);
                if (Directory.Exists(modDir) && !Directory.Exists(hiddenMod))
                    Directory.Move(modDir, hiddenMod);

                _config.ActiveProfileName = null;
            }

            var wasVanilla = IsVanillaProfile(name);
            if (wasVanilla && name == _config.ActiveProfileName)
            {
                RestoreCommonMods(_config.SavedCommonModEnabledFolders);
                _config.SavedCommonModEnabledFolders.Clear();
            }

            _sharedModService.CleanupProfileSharedMods(name, ModsRootPath, _config);
            _profileService.DeleteProfile(name, ModsRootPath, _config.SavesPath);
            _config.ProfileNames.Remove(name);
            _config.ProfileCollectionNames.Remove(name);
            _config.VanillaProfileNames.RemoveAll(n =>
                string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (_config.ProfileNames.Count > 0 && _config.ActiveProfileName == null)
            {
                var newActive = _config.ProfileNames[0];
                _profileService.SwitchProfile(null, newActive, ModsRootPath, _config.SavesPath);
                _config.ActiveProfileName = newActive;
            }

            _configService.Save(_config);
            IsConfirmingDelete = false;
            LoadProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete profile: {ex.Message}";
            IsConfirmingDelete = false;
        }
    }

    [RelayCommand]
    private void DeleteProfileAndSaves()
    {
        if (_pendingDeleteProfileName == null) return;
        var name = _pendingDeleteProfileName;
        _pendingDeleteProfileName = null;
        IsPromptingDeleteSaves = false;
        OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
        OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
        ExecuteProfileDeletion(name);
    }

    [RelayCommand]
    private void ReassignDeletedProfileSaves()
    {
        if (_pendingDeleteProfileName == null) return;

        var name = _pendingDeleteProfileName;

        string saveSourceDir;
        if (name == _config.ActiveProfileName)
            saveSourceDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
        else
            saveSourceDir = ProfileService.GetInactiveSavesPath(name, _config.SavesPath);

        if (!Directory.Exists(saveSourceDir)) return;

        var saveDirs = Directory.GetDirectories(saveSourceDir);
        if (saveDirs.Length == 0) return;

        SavesToCategorize.Clear();
        foreach (var saveDir in saveDirs)
        {
            var saveName = Path.GetFileName(saveDir);
            var item = new SaveCategoryItem { SaveFolderName = saveName };
            foreach (var profile in _config.ProfileNames)
            {
                if (!string.Equals(profile, name, StringComparison.OrdinalIgnoreCase))
                    item.AvailableProfiles.Add(profile);
            }
            SavesToCategorize.Add(item);
        }

        IsPromptingDeleteSaves = false;
        IsCategorizingSaves = true;
        OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
        OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
    }

    [RelayCommand]
    private void CancelDeleteProfileSaves()
    {
        _pendingDeleteProfileName = null;
        IsPromptingDeleteSaves = false;
        OnPropertyChanged(nameof(IsReassigningDeletedProfileSaves));
        OnPropertyChanged(nameof(CanReassignDeletedProfileSaves));
    }

    [RelayCommand]
    private void CancelDeleteProfile()
    {
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile == null) return;
        RenameProfileNewName = SelectedProfile.Name;
        IsRenamingProfile = true;
    }

    [RelayCommand]
    private void ConfirmRenameProfile()
    {
        if (SelectedProfile == null) return;

        var oldName = SelectedProfile.Name;
        var newName = RenameProfileNewName.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "Profile name cannot be empty.";
            return;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            StatusMessage = "Profile name contains invalid characters.";
            return;
        }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            IsRenamingProfile = false;
            return;
        }

        if (_config.ProfileNames.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "A profile with that name already exists.";
            return;
        }

        try
        {
            _profileService.RenameProfile(oldName, newName, ModsRootPath, _config.SavesPath);
            _sharedModService.RenameProfileSharedMods(oldName, newName, ModsRootPath, _config);

            var idx = _config.ProfileNames.FindIndex(n =>
                string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _config.ProfileNames[idx] = newName;

            if (string.Equals(_config.ActiveProfileName, oldName, StringComparison.OrdinalIgnoreCase))
                _config.ActiveProfileName = newName;

            if (_config.ProfileCollectionNames.TryGetValue(oldName, out var collections))
            {
                _config.ProfileCollectionNames.Remove(oldName);
                _config.ProfileCollectionNames[newName] = collections;
            }

            var vanillaIdx = _config.VanillaProfileNames.FindIndex(n =>
                string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
            if (vanillaIdx >= 0)
                _config.VanillaProfileNames[vanillaIdx] = newName;

            _configService.Save(_config);
            IsRenamingProfile = false;
            RenameProfileNewName = string.Empty;
            LoadProfiles();
            StatusMessage = $"Renamed \"{oldName}\" to \"{newName}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to rename profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelRenameProfile()
    {
        IsRenamingProfile = false;
        RenameProfileNewName = string.Empty;
    }
}
