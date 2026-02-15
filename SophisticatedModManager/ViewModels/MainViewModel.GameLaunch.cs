using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using SophisticatedModManager.Services;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Game launching and Steam Cloud save protection.
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private void Play()
    {
        if (_gameLauncherService.IsGameRunning())
        {
            MessageBox.Show("Stardew Valley is already running.",
                "Game Running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_config.ActiveProfileName == null)
        {
            MessageBox.Show("Please select a profile first.",
                "No Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _gameLauncherService.LaunchGame(_config.GamePath);
            StatusMessage = "Launching Stardew Valley...";
            StartSteamCloudGuard();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch game: {ex.Message}",
                "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartSteamCloudGuard()
    {
        StopSteamCloudGuard();

        var savesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
        if (!Directory.Exists(savesDir))
            Directory.CreateDirectory(savesDir);

        _activeProfileSaveNames = Directory.GetDirectories(savesDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        _savesWatcher = new FileSystemWatcher(savesDir)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _savesWatcher.Created += OnSaveDirectoryCreated;

        Task.Run(async () =>
        {
            while (_gameLauncherService.IsGameRunning())
                await Task.Delay(5000);

            await Application.Current.Dispatcher.InvokeAsync(StopSteamCloudGuard);
        });
    }

    private void StopSteamCloudGuard()
    {
        if (_savesWatcher != null)
        {
            _savesWatcher.EnableRaisingEvents = false;
            _savesWatcher.Created -= OnSaveDirectoryCreated;
            _savesWatcher.Dispose();
            _savesWatcher = null;
        }
    }

    private void OnSaveDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        var saveName = Path.GetFileName(e.FullPath);
        if (saveName == null) return;
        if (_activeProfileSaveNames.Contains(saveName)) return;

        if (IsInactiveProfileSave(saveName))
        {
            CleanAllSteamCloudSaves();
            Application.Current.Dispatcher.Invoke(StopSteamCloudGuard);
        }
    }

    private void CleanAllSteamCloudSaves()
    {
        var savesDir = ProfileService.GetActiveSavesPath(_config.SavesPath);
        if (!Directory.Exists(savesDir)) return;

        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var saveName = Path.GetFileName(dir);
            if (saveName == null) continue;
            if (_activeProfileSaveNames.Contains(saveName)) continue;

            if (IsInactiveProfileSave(saveName))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                }
            }
        }
    }

    private bool IsInactiveProfileSave(string saveName)
    {
        foreach (var profileName in _config.ProfileNames)
        {
            if (profileName == _config.ActiveProfileName) continue;

            var profileSavesDir = ProfileService.GetInactiveSavesPath(profileName, _config.SavesPath);
            if (!Directory.Exists(profileSavesDir)) continue;

            if (Directory.Exists(Path.Combine(profileSavesDir, saveName)))
                return true;
        }
        return false;
    }
}
