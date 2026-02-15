using CommunityToolkit.Mvvm.ComponentModel;
using SophisticatedModManager.Models;
using SophisticatedModManager.Services;
using System.Collections.ObjectModel;

namespace SophisticatedModManager.ViewModels;

// ==================== PROFILE MANAGEMENT ====================

public partial class ProfileItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isActive;
}

// ==================== MOD SHARING ====================

public partial class ShareTargetProfile : ObservableObject
{
    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    [ObservableProperty]
    private bool _hasVersionMismatch;
}

public partial class DuplicateModGroup : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string ModName { get; set; } = string.Empty;
    public string UniqueID { get; set; } = string.Empty;
    public bool HasVersionMismatch { get; set; }
    public ObservableCollection<DuplicateModInstance> Instances { get; set; } = new();
}

public class DuplicateModInstance
{
    public string ProfileName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public partial class DuplicateCollectionGroup : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string CollectionName { get; set; } = string.Empty;
    public bool HasIdentityMismatch { get; set; }
    public ObservableCollection<DuplicateCollectionInstance> Instances { get; set; } = new();
}

public class DuplicateCollectionInstance
{
    public string ProfileName { get; set; } = string.Empty;
    public int SubModCount { get; set; }
}

// ==================== MOD OPERATIONS ====================

public record MoveModRequest(ModEntryViewModel Mod, string TargetType, string? TargetProfileName);
public record DuplicateModRequest(ModEntryViewModel Mod, string TargetProfileName);

// ==================== FIRST-TIME SETUP ====================

public enum FolderClassification
{
    Profile,
    Collection,
    DisabledMod
}

public partial class DetectedFolderItem : ObservableObject
{
    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isLikelyProfile;

    [ObservableProperty]
    private bool _isLikelyDisabledMod;

    [ObservableProperty]
    private int _subModCount;

    [ObservableProperty]
    private FolderClassification _classification = FolderClassification.Profile;
}

public partial class SaveCategoryItem : ObservableObject
{
    [ObservableProperty]
    private string _saveFolderName = string.Empty;

    [ObservableProperty]
    private string? _assignedProfileName;

    public ObservableCollection<string> AvailableProfiles { get; set; } = new();
}

public partial class ExtractedFolderItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _hasManifest;

    [ObservableProperty]
    private bool _isSelected;
}

// ==================== NEXUS MODS ====================

public partial class NexusBrowseItem : ObservableObject
{
    [ObservableProperty]
    private int _modId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _pictureUrl = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;
}

public partial class InstallTargetOption : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _targetDir = string.Empty;
}

// ==================== MOD CONFIGURATION ====================

public partial class ConfigEntryViewModel : ObservableObject
{
    private readonly ConfigEntry _model;

    public ConfigEntry Model => _model;

    public string Key => _model.Key;
    public ConfigValueType ValueType => _model.ValueType;
    public int NestingLevel => _model.NestingLevel;

    [ObservableProperty]
    private string _stringValue = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private ObservableCollection<ConfigEntryViewModel> _children = new();

    public ConfigEntryViewModel(ConfigEntry model)
    {
        _model = model;
        StringValue = model.StringValue;
        BoolValue = model.BoolValue;

        foreach (var child in model.Children)
            Children.Add(new ConfigEntryViewModel(child));
    }

    public void ApplyToModel()
    {
        _model.StringValue = StringValue;
        _model.BoolValue = BoolValue;
        foreach (var child in Children)
            child.ApplyToModel();
    }
}

// ==================== SAVE BACKUPS ====================

public class BackupDisplayItem
{
    public SaveBackupInfo BackupInfo { get; init; } = null!;
    public string SaveNameDisplay { get; init; } = string.Empty;
    public string TimestampDisplay { get; init; } = string.Empty;
    public string RelativeTimeDisplay { get; init; } = string.Empty;
    public string SizeDisplay { get; init; } = string.Empty;
}

public partial class RestoreAllItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string SaveName { get; init; } = string.Empty;
    public SaveBackupInfo LatestBackup { get; init; } = null!;
    public string TimestampDisplay { get; init; } = string.Empty;
    public string SizeDisplay { get; init; } = string.Empty;
}
