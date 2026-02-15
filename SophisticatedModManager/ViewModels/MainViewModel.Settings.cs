using System.IO;
using CommunityToolkit.Mvvm.Input;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Settings and initial setup.
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private void OpenSettings()
    {
        SettingsSavesPath = _config.SavesPath;
        SettingsGamePath = _config.GamePath;
        SettingsShowModDescriptions = _config.ShowModDescriptions;
        SettingsResetSearchOnSwitch = _config.ResetSearchOnProfileSwitch;
        SettingsNexusApiKey = _config.NexusApiKey ?? string.Empty;
        SettingsMaxSaveBackups = _config.MaxSaveBackups;
        NexusApiKeyStatus = string.Empty;
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var previousGamePath = _config.GamePath;
        _config.SavesPath = SettingsSavesPath;
        _config.GamePath = SettingsGamePath;
        _config.ShowModDescriptions = SettingsShowModDescriptions;
        _config.ResetSearchOnProfileSwitch = SettingsResetSearchOnSwitch;
        _config.NexusApiKey = string.IsNullOrWhiteSpace(SettingsNexusApiKey) ? null : SettingsNexusApiKey.Trim();
        _config.MaxSaveBackups = Math.Clamp(SettingsMaxSaveBackups, 0, 10);
        ShowModDescriptions = SettingsShowModDescriptions;

        if (!string.IsNullOrWhiteSpace(_config.NexusApiKey))
        {
            _nexusService.Configure(_config.NexusApiKey);
            IsNexusConfigured = true;
            _ = FetchEndorsementsAsync();
        }
        else
        {
            _nexusService.Configure("");
            IsNexusConfigured = false;
            _endorsedModIds.Clear();
        }

        _configService.Save(_config);
        IsSettingsVisible = false;
        StatusMessage = "Settings saved.";

        if (previousGamePath != _config.GamePath)
        {
            _config.ProfileNames.Clear();
            _config.ActiveProfileName = null;
            _configService.Save(_config);
            RunFirstLoadDetection();
        }
        else
        {
            LoadProfiles();
        }
    }

    [RelayCommand]
    private void BrowseSavesPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Stardew Valley Saves Folder"
        };
        if (dialog.ShowDialog() == true)
            SettingsSavesPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseGamePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Stardew Valley Game Folder"
        };
        if (dialog.ShowDialog() == true)
            SettingsGamePath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseSetupGamePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Stardew Valley Game Folder"
        };
        if (dialog.ShowDialog() == true)
            SetupGamePath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseSetupSavesPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Stardew Valley Saves Folder"
        };
        if (dialog.ShowDialog() == true)
            SetupSavesPath = dialog.FolderName;
    }

    [RelayCommand]
    private void SelectDetectedPath(string path)
    {
        SetupGamePath = path;
    }

    private async Task ScanForGameInstallationsAsync()
    {
        IsScanningForGame = true;
        DetectedGamePaths.Clear();

        var found = await Task.Run(() =>
        {
            var paths = new List<string>();

            foreach (var candidate in GetSteamLibraryPaths())
            {
                var sdvPath = Path.Combine(candidate, "steamapps", "common", "Stardew Valley");
                if (IsValidGamePath(sdvPath) && !paths.Contains(sdvPath, StringComparer.OrdinalIgnoreCase))
                    paths.Add(sdvPath);
            }

            var knownPaths = new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley",
                @"C:\Program Files\Steam\steamapps\common\Stardew Valley",
                @"C:\Program Files (x86)\GOG Galaxy\Games\Stardew Valley",
                @"C:\Program Files\GOG Galaxy\Games\Stardew Valley",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Steam\steamapps\common\Stardew Valley"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Steam\steamapps\common\Stardew Valley"),
            };

            foreach (var path in knownPaths)
            {
                if (IsValidGamePath(path) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    paths.Add(path);
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var steamCommon = Path.Combine(drive.Name, @"SteamLibrary\steamapps\common\Stardew Valley");
                if (IsValidGamePath(steamCommon) && !paths.Contains(steamCommon, StringComparer.OrdinalIgnoreCase))
                    paths.Add(steamCommon);

                var gamesDir = Path.Combine(drive.Name, @"Games\Stardew Valley");
                if (IsValidGamePath(gamesDir) && !paths.Contains(gamesDir, StringComparer.OrdinalIgnoreCase))
                    paths.Add(gamesDir);
            }

            return paths;
        });

        foreach (var path in found)
            DetectedGamePaths.Add(path);

        if (found.Count == 1)
            SetupGamePath = found[0];

        IsScanningForGame = false;
    }

    private static bool IsValidGamePath(string path)
    {
        return Directory.Exists(path)
               && (File.Exists(Path.Combine(path, "Stardew Valley.exe"))
                   || File.Exists(Path.Combine(path, "StardewModdingAPI.exe")));
    }

    private static List<string> GetSteamLibraryPaths()
    {
        var paths = new List<string>();

        var steamPaths = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };

        foreach (var steamPath in steamPaths)
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;

            if (!paths.Contains(steamPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(steamPath);

            try
            {
                foreach (var line in File.ReadAllLines(vdfPath))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;

                    var parts = trimmed.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var libPath = parts[^1].Trim('"').Replace(@"\\", @"\");
                    if (Directory.Exists(libPath) && !paths.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                        paths.Add(libPath);
                }
            }
            catch
            {
            }
        }

        return paths;
    }

    [RelayCommand]
    private void ConfirmSetupPaths()
    {
        if (string.IsNullOrWhiteSpace(SetupGamePath))
        {
            SetupPathError = "Game path cannot be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SetupSavesPath))
        {
            SetupPathError = "Saves path cannot be empty.";
            return;
        }

        if (!Directory.Exists(SetupGamePath))
        {
            SetupPathError = "Game path does not exist. Please select a valid folder.";
            return;
        }

        _config.GamePath = SetupGamePath;
        _config.SavesPath = SetupSavesPath;
        SettingsGamePath = SetupGamePath;
        SettingsSavesPath = SetupSavesPath;
        _configService.Save(_config);

        IsSettingUpPaths = false;
        SetupPathError = string.Empty;

        LoadProfiles();

        if (_config.ProfileNames.Count == 0)
            RunFirstLoadDetection();
    }
}
