using System.Diagnostics;
using System.IO;

namespace SophisticatedModManager.Services;

public class ProfileService : IProfileService
{
    public static string ProfileModFolder(string profileName) => "[PROFILE] " + profileName;

    public static string? ParseProfileName(string folderName)
    {
        var stripped = folderName.TrimStart('.');
        const string prefix = "[PROFILE] ";
        return stripped.StartsWith(prefix) ? stripped[prefix.Length..] : null;
    }

    /// <summary>
    /// Gets the path to a profile's mod folder (active state, no dot prefix).
    /// </summary>
    public static string GetActiveProfileModPath(string profileName, string modsRoot)
    {
        return Path.Combine(modsRoot, ProfileModFolder(profileName));
    }

    /// <summary>
    /// Gets the path to a profile's mod folder (inactive state, dot prefix).
    /// </summary>
    public static string GetInactiveProfileModPath(string profileName, string modsRoot)
    {
        return Path.Combine(modsRoot, "." + ProfileModFolder(profileName));
    }

    /// <summary>
    /// Gets the path to a profile's saves folder (inactive state, dot prefix).
    /// </summary>
    public static string GetInactiveSavesPath(string profileName, string savesPath)
    {
        return Path.Combine(savesPath, "." + profileName + "Saves");
    }

    /// <summary>
    /// Gets the path to active saves folder (no profile prefix).
    /// </summary>
    public static string GetActiveSavesPath(string savesPath)
    {
        return Path.Combine(savesPath, "Saves");
    }

    public void CreateProfile(string name, string modsRootPath, string savesPath)
    {
        var modDir = GetInactiveProfileModPath(name, modsRootPath);
        if (!Directory.Exists(modDir))
            Directory.CreateDirectory(modDir);

        var saveDir = GetInactiveSavesPath(name, savesPath);
        if (!Directory.Exists(saveDir))
            Directory.CreateDirectory(saveDir);
    }

    public void DeleteProfile(string name, string modsRootPath, string savesPath)
    {
        var modDir = GetActiveProfileModPath(name, modsRootPath);
        var modDirHidden = GetInactiveProfileModPath(name, modsRootPath);

        if (Directory.Exists(modDir))
            Directory.Delete(modDir, true);
        if (Directory.Exists(modDirHidden))
            Directory.Delete(modDirHidden, true);

        var saveDir = GetActiveSavesPath(savesPath);
        var saveDirHidden = GetInactiveSavesPath(name, savesPath);

        if (Directory.Exists(saveDirHidden))
            Directory.Delete(saveDirHidden, true);
    }

    public void RenameProfile(string oldName, string newName, string modsRootPath, string savesPath)
    {
        // Rename mod folder (active or inactive)
        var modDir = GetActiveProfileModPath(oldName, modsRootPath);
        var modDirHidden = GetInactiveProfileModPath(oldName, modsRootPath);

        if (Directory.Exists(modDir))
            Directory.Move(modDir, GetActiveProfileModPath(newName, modsRootPath));
        else if (Directory.Exists(modDirHidden))
            Directory.Move(modDirHidden, GetInactiveProfileModPath(newName, modsRootPath));

        // Rename saves folder (only if inactive / hidden)
        var savesDirHidden = GetInactiveSavesPath(oldName, savesPath);
        if (Directory.Exists(savesDirHidden))
            Directory.Move(savesDirHidden, GetInactiveSavesPath(newName, savesPath));
    }

    public void SwitchProfile(string? fromName, string toName, string modsRootPath, string savesPath)
    {
        var savesDir = GetActiveSavesPath(savesPath);

        if (fromName != null)
        {
            var fromSaveHidden = GetInactiveSavesPath(fromName, savesPath);
            if (Directory.Exists(savesDir))
            {
                if (Directory.Exists(fromSaveHidden))
                    Directory.Delete(fromSaveHidden, true);
                Directory.Move(savesDir, fromSaveHidden);
            }
        }

        var toSaveHidden = GetInactiveSavesPath(toName, savesPath);
        if (Directory.Exists(toSaveHidden))
        {
            if (!Directory.Exists(savesDir))
                Directory.Move(toSaveHidden, savesDir);
        }
        else
        {
            if (!Directory.Exists(savesDir))
                Directory.CreateDirectory(savesDir);
        }

        if (fromName != null)
        {
            var fromModDir = GetActiveProfileModPath(fromName, modsRootPath);
            var fromModHidden = GetInactiveProfileModPath(fromName, modsRootPath);
            if (Directory.Exists(fromModDir) && !Directory.Exists(fromModHidden))
            {
                Directory.Move(fromModDir, fromModHidden);
            }
        }

        var toModHidden = GetInactiveProfileModPath(toName, modsRootPath);
        var toModDir = GetActiveProfileModPath(toName, modsRootPath);
        if (Directory.Exists(toModHidden) && !Directory.Exists(toModDir))
        {
            Directory.Move(toModHidden, toModDir);
        }
        else if (!Directory.Exists(toModDir))
        {
            Directory.CreateDirectory(toModDir);
        }
    }

    public void MigrateProfileFolders(List<string> profileNames, string modsRootPath)
    {
        if (!Directory.Exists(modsRootPath)) return;

        foreach (var name in profileNames)
        {
            var oldActive = Path.Combine(modsRootPath, name);
            var newActive = GetActiveProfileModPath(name, modsRootPath);
            if (Directory.Exists(oldActive) && !Directory.Exists(newActive))
                Directory.Move(oldActive, newActive);

            var oldInactive = Path.Combine(modsRootPath, "." + name);
            var newInactive = GetInactiveProfileModPath(name, modsRootPath);
            if (Directory.Exists(oldInactive) && !Directory.Exists(newInactive))
                Directory.Move(oldInactive, newInactive);
        }
    }

    public bool IsGameRunning()
    {
        return Process.GetProcessesByName("StardewModdingAPI").Length > 0
               || Process.GetProcessesByName("Stardew Valley").Length > 0;
    }
}
