using System.IO;
using System.Text.Json;
using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public class ModService : IModService
{
    public List<ModEntry> GetModsForProfile(string profileName, string modsRootPath, List<string> collectionNames)
    {
        var mods = new List<ModEntry>();

        var profileDir = ProfileService.GetActiveProfileModPath(profileName, modsRootPath);
        if (!Directory.Exists(profileDir))
        {
            profileDir = ProfileService.GetInactiveProfileModPath(profileName, modsRootPath);
            if (!Directory.Exists(profileDir))
                return mods;
        }

        var collectionFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in collectionNames)
        {
            collectionFolders.Add(name);
            collectionFolders.Add(FolderNameHelper.ToDisabled(name));
        }

        foreach (var dir in Directory.GetDirectories(profileDir))
        {
            var folderName = Path.GetFileName(dir);

            if (collectionFolders.Contains(folderName))
            {
                var collection = LoadCollection(dir, folderName, isCommon: false);
                DetectJunctionState(collection, dir);
                mods.Add(collection);
                continue;
            }

            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest))
                continue;

            var entry = FileSystemHelpers.ReadManifest(manifest);
            if (entry == null) continue;

            entry.FolderName = folderName;
            entry.FolderPath = dir;
            entry.IsEnabled = FolderNameHelper.IsEnabled(folderName);
            entry.IsCommon = false;
            DetectJunctionState(entry, dir);
            mods.Add(entry);
        }

        return mods.OrderBy(m => m.Name).ToList();
    }

    public List<ModEntry> GetCommonMods(string modsRootPath, List<string> profileNames, List<string> commonCollectionNames)
    {
        var mods = new List<ModEntry>();
        if (!Directory.Exists(modsRootPath))
            return mods;

        var profileFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in profileNames)
        {
            profileFolders.Add(ProfileService.ProfileModFolder(name));
            profileFolders.Add(FolderNameHelper.ToDisabled(ProfileService.ProfileModFolder(name)));
        }

        var collectionFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in commonCollectionNames)
        {
            collectionFolders.Add(name);
            collectionFolders.Add(FolderNameHelper.ToDisabled(name));
        }

        foreach (var dir in Directory.GetDirectories(modsRootPath))
        {
            var folderName = Path.GetFileName(dir);

            if (folderName.Equals(ISharedModService.SharedPoolFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (profileFolders.Contains(folderName))
                continue;

            if (collectionFolders.Contains(folderName))
            {
                var collection = LoadCollection(dir, folderName, isCommon: true);
                mods.Add(collection);
                continue;
            }

            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest))
                continue;

            var entry = FileSystemHelpers.ReadManifest(manifest);
            if (entry == null) continue;

            entry.FolderName = folderName;
            entry.FolderPath = dir;
            entry.IsEnabled = FolderNameHelper.IsEnabled(folderName);
            entry.IsCommon = true;
            mods.Add(entry);
        }

        return mods.OrderBy(m => m.Name).ToList();
    }

    private static ModEntry LoadCollection(string collectionDir, string folderName, bool isCommon)
    {
        var cleanName = FolderNameHelper.GetCleanName(folderName);
        var collection = new ModEntry
        {
            FolderName = folderName,
            FolderPath = collectionDir,
            Name = cleanName,
            Author = "Collection",
            Version = "",
            Description = "",
            UniqueID = $"collection:{cleanName}",
            IsEnabled = FolderNameHelper.IsEnabled(folderName),
            IsCommon = isCommon,
            IsCollection = true,
            SubMods = new List<ModEntry>()
        };

        foreach (var subDir in Directory.GetDirectories(collectionDir))
        {
            var subFolderName = Path.GetFileName(subDir);
            var manifest = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifest)) continue;

            var subEntry = FileSystemHelpers.ReadManifest(manifest);
            if (subEntry == null) continue;

            subEntry.FolderName = subFolderName;
            subEntry.FolderPath = subDir;
            subEntry.IsEnabled = FolderNameHelper.IsEnabled(subFolderName);
            subEntry.IsCommon = isCommon;
            collection.SubMods.Add(subEntry);
        }

        collection.SubMods = collection.SubMods.OrderBy(m => m.Name).ToList();
        collection.Description = $"{collection.SubMods.Count} mod(s)";
        return collection;
    }

    public void SetModEnabled(ModEntry mod, bool enabled)
    {
        var parentDir = Path.GetDirectoryName(mod.FolderPath)!;
        var currentName = Path.GetFileName(mod.FolderPath);
        var newName = enabled ? FolderNameHelper.ToEnabled(currentName) : FolderNameHelper.ToDisabled(currentName);

        if (newName == currentName) return;

        var newPath = Path.Combine(parentDir, newName);
        Directory.Move(mod.FolderPath, newPath);
        mod.FolderPath = newPath;
        mod.FolderName = newName;
        mod.IsEnabled = enabled;
    }

    public void DeleteMod(ModEntry mod)
    {
        if (!Directory.Exists(mod.FolderPath)) return;

        if (new DirectoryInfo(mod.FolderPath).Attributes.HasFlag(FileAttributes.ReparsePoint))
            Directory.Delete(mod.FolderPath, false);
        else
            Directory.Delete(mod.FolderPath, true);
    }

    public void MoveMod(ModEntry mod, string targetDirectoryPath)
    {
        var destPath = Path.Combine(targetDirectoryPath, mod.FolderName);
        if (Directory.Exists(destPath))
            throw new InvalidOperationException($"A mod folder named '{mod.FolderName}' already exists in the target location.");
        if (!Directory.Exists(targetDirectoryPath))
            Directory.CreateDirectory(targetDirectoryPath);
        Directory.Move(mod.FolderPath, destPath);
        mod.FolderPath = destPath;
    }

    public void DuplicateMod(ModEntry mod, string targetDirectoryPath)
    {
        var destPath = Path.Combine(targetDirectoryPath, mod.FolderName);
        if (Directory.Exists(destPath))
            throw new InvalidOperationException($"A mod folder named '{mod.FolderName}' already exists in the target location.");
        if (!Directory.Exists(targetDirectoryPath))
            Directory.CreateDirectory(targetDirectoryPath);
        FileSystemHelpers.CopyDirectory(mod.FolderPath, destPath);
    }

    private static void DetectJunctionState(ModEntry entry, string dir)
    {
        var info = new DirectoryInfo(dir);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            entry.IsShared = true;
            var target = info.ResolveLinkTarget(false);
            entry.SharedFolderName = target != null ? Path.GetFileName(target.FullName) : Path.GetFileName(dir);
        }
    }

}
