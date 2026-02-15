using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SophisticatedModManager.Models;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

public partial class ModEntryViewModel : ObservableObject
{
    private readonly IModService _modService;
    private readonly IModConfigService _modConfigService;
    private readonly ModEntry _model;

    public ModEntry Model => _model;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isCommon;

    [ObservableProperty]
    private bool _isCollection;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<ModEntryViewModel> _subMods = new();

    [ObservableProperty]
    private int? _nexusModId;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _hasConfig;

    [ObservableProperty]
    private bool _isEndorsed;

    [ObservableProperty]
    private bool _isEndorsing;

    [ObservableProperty]
    private bool _isShared;

    [ObservableProperty]
    private string? _sharedFolderName;

    public bool CanEndorse => NexusModId != null;

    public ModEntryViewModel(ModEntry model, IModService modService, IModConfigService modConfigService)
    {
        _model = model;
        _modService = modService;
        _modConfigService = modConfigService;

        Name = model.Name;
        Author = model.Author;
        Version = model.Version;
        Description = model.Description;
        IsEnabled = model.IsEnabled;
        IsCommon = model.IsCommon;
        IsCollection = model.IsCollection;
        NexusModId = model.NexusModId;
        IsShared = model.IsShared;
        SharedFolderName = model.SharedFolderName;
        HasConfig = !model.IsCollection && modConfigService.HasConfig(model.FolderPath);

        if (model.IsCollection)
        {
            foreach (var subMod in model.SubMods)
                SubMods.Add(new ModEntryViewModel(subMod, modService, modConfigService));
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        try
        {
            _modService.SetModEnabled(_model, value);
        }
        catch
        {
            _isEnabled = !value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
}
