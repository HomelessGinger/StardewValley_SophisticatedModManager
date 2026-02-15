using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.IO.Compression;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCompress.Archives;
using SharpCompress.Common;
using SophisticatedModManager.Models;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IProfileService _profileService;
    private readonly IModService _modService;
    private readonly IGameLauncherService _gameLauncherService;
    private readonly IModsDetectionService _detectionService;
    private readonly INexusModsService _nexusService;
    private readonly IModConfigService _modConfigService;
    private readonly ISharedModService _sharedModService;
    private readonly ISaveBackupService _saveBackupService;
    private AppConfig _config;

    [ObservableProperty]
    private ObservableCollection<ProfileItem> _profiles = new();

    [ObservableProperty]
    private ProfileItem? _selectedProfile;

    [ObservableProperty]
    private ObservableCollection<ModEntryViewModel> _profileMods = new();

    [ObservableProperty]
    private ObservableCollection<ModEntryViewModel> _commonMods = new();

    public bool HasCollections => ProfileMods.Any(m => m.IsCollection) || CommonMods.Any(m => m.IsCollection);

    public bool IsActiveProfileVanilla => _config.VanillaProfileNames
        .Contains(_config.ActiveProfileName ?? "", StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _commonModsOnTop = true;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isCreatingProfile;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private bool _isPromptingDeleteSaves;

    private string? _pendingDeleteProfileName;

    public bool IsReassigningDeletedProfileSaves => _pendingDeleteProfileName != null;

    public bool CanReassignDeletedProfileSaves =>
        _pendingDeleteProfileName != null && _config.ProfileNames.Count > 1;

    [ObservableProperty]
    private bool _isRenamingProfile;

    [ObservableProperty]
    private string _renameProfileNewName = string.Empty;

    [ObservableProperty]
    private string _addModsStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSelectingModFolders;

    [ObservableProperty]
    private string _extractedArchiveName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ExtractedFolderItem> _extractedFolders = new();

    private List<string>? _pendingExtractTempDirs;
    private string? _pendingProfileModDir;

    [ObservableProperty]
    private string _settingsSavesPath = string.Empty;

    [ObservableProperty]
    private string _settingsGamePath = string.Empty;

    [ObservableProperty]
    private bool _showModDescriptions = true;

    [ObservableProperty]
    private bool _settingsShowModDescriptions = true;

    [ObservableProperty]
    private bool _settingsResetSearchOnSwitch;

    [ObservableProperty]
    private string _modSearchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isConfirmingModDelete;

    [ObservableProperty]
    private ModEntryViewModel? _modPendingDelete;

    [ObservableProperty]
    private bool _isSettingUpPaths;

    [ObservableProperty]
    private string _setupGamePath = string.Empty;

    [ObservableProperty]
    private string _setupSavesPath = string.Empty;

    [ObservableProperty]
    private string _setupPathError = string.Empty;

    [ObservableProperty]
    private bool _isScanningForGame;

    [ObservableProperty]
    private ObservableCollection<string> _detectedGamePaths = new();

    [ObservableProperty]
    private bool _isFirstLoadActive;

    [ObservableProperty]
    private bool _isPromptingFirstProfile;

    [ObservableProperty]
    private string _firstProfileName = string.Empty;

    [ObservableProperty]
    private bool _isAskingToOrganize;

    [ObservableProperty]
    private string _organizeProfileName = string.Empty;

    [ObservableProperty]
    private bool _isVerifyingFolders;

    [ObservableProperty]
    private ObservableCollection<DetectedFolderItem> _detectedFolders = new();

    [ObservableProperty]
    private bool _isCategorizingSaves;

    [ObservableProperty]
    private ObservableCollection<SaveCategoryItem> _savesToCategorize = new();

    private DetectionResult? _lastDetectionResult;

    private bool _isRefreshDetection;
    public bool IsRefreshDetection => _isRefreshDetection;

    [ObservableProperty]
    private bool _isNexusConfigured;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private int _updatesAvailableCount;

    [ObservableProperty]
    private string _settingsNexusApiKey = string.Empty;

    [ObservableProperty]
    private string _nexusApiKeyStatus = string.Empty;

    [ObservableProperty]
    private bool _isNexusBrowsing;

    [ObservableProperty]
    private ObservableCollection<NexusBrowseItem> _nexusBrowseResults = new();

    [ObservableProperty]
    private string _nexusBrowseCategory = "Trending";

    [ObservableProperty]
    private bool _isNexusBrowseLoading;

    [ObservableProperty]
    private string _nexusInstallUrl = string.Empty;

    [ObservableProperty]
    private bool _isInstallingFromNexus;

    [ObservableProperty]
    private string _nexusInstallStatus = string.Empty;

    [ObservableProperty]
    private bool _nexusInstallToActiveProfile = true;

    [ObservableProperty]
    private bool _canLoadMoreNexus;

    [ObservableProperty]
    private bool _isViewingModDetail;

    [ObservableProperty]
    private NexusBrowseItem? _modDetailItem;

    [ObservableProperty]
    private string _modDetailDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NexusModFile> _modDetailFiles = new();

    [ObservableProperty]
    private bool _isLoadingModDetail;

    [ObservableProperty]
    private bool _isChoosingInstallTarget;

    [ObservableProperty]
    private ObservableCollection<InstallTargetOption> _installTargetOptions = new();

    [ObservableProperty]
    private InstallTargetOption? _selectedInstallTarget;

    [ObservableProperty]
    private bool _isCreatingCollection;

    [ObservableProperty]
    private string _newCollectionName = string.Empty;

    [ObservableProperty]
    private bool _createCollectionIsCommon = true;

    [ObservableProperty]
    private bool _isConvertingCollectionToProfile;

    [ObservableProperty]
    private string _convertCollectionProfileName = string.Empty;

    private ModEntryViewModel? _collectionToConvert;

    [ObservableProperty]
    private bool _isConvertingProfileToCollection;

    [ObservableProperty]
    private string _convertProfileCollectionName = string.Empty;

    [ObservableProperty]
    private bool _convertProfileToCommon = true;

    [ObservableProperty]
    private string? _convertProfileTargetProfileName;

    private string? _profileToConvert;

    [ObservableProperty]
    private bool _isEditingConfig;

    [ObservableProperty]
    private string _configModName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConfigEntryViewModel> _configEntries = new();

    [ObservableProperty]
    private string _configSaveStatus = string.Empty;

    private string? _configModFolderPath;

    [ObservableProperty]
    private bool _isViewingChangelog;

    [ObservableProperty]
    private string _changelogModName = string.Empty;

    [ObservableProperty]
    private string _changelogText = string.Empty;

    [ObservableProperty]
    private bool _isLoadingChangelog;

    private List<int> _recentlyUpdatedModIds = new();
    private int _recentlyUpdatedLoadIndex;
    private const int BrowseBatchSize = 10;
    private TaskCompletionSource<string?>? _installTargetTcs;
    private HashSet<int> _endorsedModIds = new();
    private FileSystemWatcher? _savesWatcher;
    private HashSet<string> _activeProfileSaveNames = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isPromptingSharedModUpdate;

    [ObservableProperty]
    private ModEntryViewModel? _sharedModUpdatePendingMod;

    [ObservableProperty]
    private bool _isShowingShareDialog;

    [ObservableProperty]
    private ModEntryViewModel? _modToShare;

    [ObservableProperty]
    private ObservableCollection<ShareTargetProfile> _shareTargetProfiles = new();

    [ObservableProperty]
    private bool _isShowingDuplicates;

    [ObservableProperty]
    private ObservableCollection<DuplicateModGroup> _detectedDuplicates = new();

    [ObservableProperty]
    private bool _isShowingDuplicateCollections;

    [ObservableProperty]
    private ObservableCollection<DuplicateCollectionGroup> _detectedDuplicateCollections = new();

    [ObservableProperty]
    private bool _isViewingBackups;

    [ObservableProperty]
    private ObservableCollection<BackupDisplayItem> _backupItems = new();

    [ObservableProperty]
    private string _currentSaveTimestampDisplay = string.Empty;

    [ObservableProperty]
    private BackupDisplayItem? _selectedBackup;

    [ObservableProperty]
    private bool _isConfirmingRestore;

    [ObservableProperty]
    private int _settingsMaxSaveBackups = 1;

    [ObservableProperty]
    private bool _isRestoreAllMode;

    [ObservableProperty]
    private ObservableCollection<RestoreAllItem> _restoreAllItems = new();

    private string ModsRootPath => Path.Combine(_config.GamePath, "Mods");

    public List<string> CommonCollectionNames => _config.CommonCollectionNames;
    public string GetModsRootPath() => ModsRootPath;
    public List<string> GetProfileCollectionNames(string profileName) =>
        _config.ProfileCollectionNames.GetValueOrDefault(profileName, new List<string>());

    public void MoveModToCollection(ModEntryViewModel modVm, string collectionPath)
    {
        try
        {
            _modService.MoveMod(modVm.Model, collectionPath);
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
            StatusMessage = $"Moved \"{modVm.Name}\" to collection.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to move mod: {ex.Message}";
        }
    }

    public MainViewModel(
        IConfigService configService,
        IProfileService profileService,
        IModService modService,
        IGameLauncherService gameLauncherService,
        IModsDetectionService detectionService,
        INexusModsService nexusService,
        IModConfigService modConfigService,
        ISharedModService sharedModService,
        ISaveBackupService saveBackupService)
    {
        _configService = configService;
        _profileService = profileService;
        _modService = modService;
        _gameLauncherService = gameLauncherService;
        _detectionService = detectionService;
        _nexusService = nexusService;
        _modConfigService = modConfigService;
        _sharedModService = sharedModService;
        _saveBackupService = saveBackupService;
        _config = configService.Load();

        SettingsSavesPath = _config.SavesPath;
        SettingsGamePath = _config.GamePath;
        ShowModDescriptions = _config.ShowModDescriptions;

        if (!string.IsNullOrWhiteSpace(_config.NexusApiKey))
        {
            _nexusService.Configure(_config.NexusApiKey);
            IsNexusConfigured = true;
            SettingsNexusApiKey = _config.NexusApiKey;
            _ = FetchEndorsementsAsync();
        }

        if (!configService.Exists())
        {
            SetupSavesPath = _config.SavesPath;
            IsSettingUpPaths = true;
            _ = ScanForGameInstallationsAsync();
            return;
        }

        if (_config.ProfileNames.Count > 0)
            _profileService.MigrateProfileFolders(_config.ProfileNames, ModsRootPath);

        LoadProfiles();
        ValidateSharedPoolOnStartup();

        if (_config.ProfileNames.Count == 0)
            RunFirstLoadDetection();
    }
    private async Task FetchEndorsementsAsync()
    {
        try
        {
            _endorsedModIds = await _nexusService.GetUserEndorsedModIdsAsync();
            ApplyEndorsementState();
        }
        catch
        {
        }
    }

    private void ApplyEndorsementState()
    {
        foreach (var mod in ProfileMods.Concat(CommonMods))
        {
            if (mod.NexusModId != null)
                mod.IsEndorsed = _endorsedModIds.Contains(mod.NexusModId.Value);

            if (mod.IsCollection)
            {
                foreach (var sub in mod.SubMods)
                {
                    if (sub.NexusModId != null)
                        sub.IsEndorsed = _endorsedModIds.Contains(sub.NexusModId.Value);
                }
            }
        }
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var name in _config.ProfileNames)
        {
            Profiles.Add(new ProfileItem
            {
                Name = name,
                IsActive = name == _config.ActiveProfileName
            });
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.IsActive)
                          ?? Profiles.FirstOrDefault();

        if (SelectedProfile != null)
            LoadModsForProfile(SelectedProfile.Name);
    }

    private void LoadModsForProfile(string profileName)
    {
        if (_config.ResetSearchOnProfileSwitch)
            ModSearchText = string.Empty;
        ProfileMods.Clear();
        CommonMods.Clear();

        if (!Directory.Exists(ModsRootPath))
        {
            StatusMessage = "Mods folder not found. Check game path in Settings.";
            return;
        }

        var profileCollections = _config.ProfileCollectionNames
            .GetValueOrDefault(profileName, new List<string>());
        var profileModEntries = _modService.GetModsForProfile(profileName, ModsRootPath, profileCollections);
        foreach (var mod in profileModEntries)
            ProfileMods.Add(new ModEntryViewModel(mod, _modService, _modConfigService));

        if (!IsVanillaProfile(profileName))
        {
            var commonModEntries = _modService.GetCommonMods(ModsRootPath, _config.ProfileNames, _config.CommonCollectionNames);
            foreach (var mod in commonModEntries)
                CommonMods.Add(new ModEntryViewModel(mod, _modService, _modConfigService));
        }

        StatusMessage = IsVanillaProfile(profileName)
            ? $"Profile: {profileName} — Vanilla (no mods)"
            : $"Profile: {profileName} — {profileModEntries.Count} mod(s)";
        OnPropertyChanged(nameof(HasCollections));

        ApplyEndorsementState();

        if (!string.IsNullOrWhiteSpace(ModSearchText))
            ApplyModSearchFilter(ModSearchText);
    }
    partial void OnSelectedProfileChanged(ProfileItem? value)
    {
        if (value == null) return;

        if (value.Name != _config.ActiveProfileName)
        {
            SwitchToProfile(value);
        }
        else
        {
            LoadModsForProfile(value.Name);
        }
    }

    private void SwitchToProfile(ProfileItem profile)
    {
        if (_gameLauncherService.IsGameRunning())
        {
            MessageBox.Show("Please close Stardew Valley before switching profiles.",
                "Game Running", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var fromVanilla = IsVanillaProfile(_config.ActiveProfileName);
            var toVanilla = IsVanillaProfile(profile.Name);

            if (_config.ActiveProfileName != null && !fromVanilla)
                _sharedModService.SaveSharedModConfigs(_config.ActiveProfileName, ModsRootPath, _config);

            _profileService.SwitchProfile(_config.ActiveProfileName, profile.Name,
                ModsRootPath, _config.SavesPath);

            if (!toVanilla)
                _sharedModService.RestoreSharedModConfigs(profile.Name, ModsRootPath, _config);

            if (!fromVanilla && toVanilla)
            {
                _config.SavedCommonModEnabledFolders = GetEnabledCommonModFolders();
                DisableAllCommonMods();
            }
            else if (fromVanilla && !toVanilla)
            {
                RestoreCommonMods(_config.SavedCommonModEnabledFolders);
                _config.SavedCommonModEnabledFolders.Clear();
            }

            foreach (var p in Profiles)
                p.IsActive = p.Name == profile.Name;

            _config.ActiveProfileName = profile.Name;
            _configService.Save(_config);

            OnPropertyChanged(nameof(IsActiveProfileVanilla));
            LoadModsForProfile(profile.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to switch profile: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsVanillaProfile(string? profileName) =>
        profileName != null && _config.VanillaProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase);

    private List<string> GetEnabledCommonModFolders()
    {
        if (!Directory.Exists(ModsRootPath)) return new();

        var profileFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _config.ProfileNames)
        {
            profileFolders.Add(ProfileService.ProfileModFolder(name));
            profileFolders.Add(FolderNameHelper.ToDisabled(ProfileService.ProfileModFolder(name)));
        }

        var enabled = new List<string>();
        foreach (var dir in Directory.GetDirectories(ModsRootPath))
        {
            var folderName = Path.GetFileName(dir);
            if (FolderNameHelper.IsDisabled(folderName)) continue;
            if (profileFolders.Contains(folderName)) continue;
            enabled.Add(folderName);
        }
        return enabled;
    }

    private void DisableAllCommonMods()
    {
        if (!Directory.Exists(ModsRootPath)) return;

        var profileFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _config.ProfileNames)
        {
            profileFolders.Add(ProfileService.ProfileModFolder(name));
            profileFolders.Add(FolderNameHelper.ToDisabled(ProfileService.ProfileModFolder(name)));
        }

        foreach (var dir in Directory.GetDirectories(ModsRootPath))
        {
            var folderName = Path.GetFileName(dir);
            if (FolderNameHelper.IsDisabled(folderName)) continue;
            if (profileFolders.Contains(folderName)) continue;

            var hiddenPath = Path.Combine(ModsRootPath, FolderNameHelper.ToDisabled(folderName));
            if (!Directory.Exists(hiddenPath))
                Directory.Move(dir, hiddenPath);
        }
    }

    private void RestoreCommonMods(List<string> enabledFolders)
    {
        if (!Directory.Exists(ModsRootPath)) return;

        foreach (var folderName in enabledFolders)
        {
            var hiddenPath = Path.Combine(ModsRootPath, FolderNameHelper.ToDisabled(folderName));
            var visiblePath = Path.Combine(ModsRootPath, folderName);
            if (Directory.Exists(hiddenPath) && !Directory.Exists(visiblePath))
                Directory.Move(hiddenPath, visiblePath);
        }
    }

    [RelayCommand]
    private static void OpenWiki()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/HomelessGinger/StardewValley_SophisticatedModManager/wiki",
            UseShellExecute = true
        });
    }

    public void HandleExternalDrop(string[] paths, bool isCommon)
    {
        if (SelectedProfile == null && !isCommon)
        {
            StatusMessage = "Select a profile first.";
            return;
        }

        string targetDir;
        if (isCommon)
        {
            targetDir = ModsRootPath;
        }
        else
        {
            targetDir = Path.Combine(ModsRootPath, ProfileService.ProfileModFolder(SelectedProfile!.Name));
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
        }

        BackupSavesBeforeModInstall(targetDir);

        int added = 0;
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (File.Exists(Path.Combine(path, "manifest.json")))
                    {
                        var destDir = Path.Combine(targetDir, Path.GetFileName(path));
                        if (Directory.Exists(destDir))
                            Directory.Delete(destDir, true);
                        FileSystemHelpers.CopyDirectory(path, destDir);
                        added++;
                    }
                    else
                    {
                        StatusMessage = $"Folder \"{Path.GetFileName(path)}\" has no manifest.json — skipped.";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add \"{Path.GetFileName(path)}\": {ex.Message}";
            }
        }

        if (added > 0)
        {
            StatusMessage = $"Added {added} mod(s) via drag & drop.";
            if (SelectedProfile != null)
                LoadModsForProfile(SelectedProfile.Name);
        }
    }
}
