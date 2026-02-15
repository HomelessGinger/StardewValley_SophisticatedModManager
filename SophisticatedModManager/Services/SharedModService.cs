using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public class SharedModService : ISharedModService
{
    public bool IsJunction(string path)
    {
        if (!Directory.Exists(path)) return false;
        var info = new DirectoryInfo(path);
        return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    public void CreateJunction(string linkPath, string targetPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to create junction: {error}");
        }
    }

    public void RemoveJunction(string linkPath)
    {
        if (!Directory.Exists(linkPath)) return;

        if (!IsJunction(linkPath))
            throw new InvalidOperationException($"Path is not a junction: {linkPath}");

        Directory.Delete(linkPath, false);
    }

    public void ShareMod(ModEntry mod, List<string> targetProfiles, string modsRoot, AppConfig config)
    {
        if (targetProfiles.Count < 2)
            throw new InvalidOperationException("Must select at least 2 profiles to share a mod.");

        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        if (!Directory.Exists(sharedPoolDir))
            Directory.CreateDirectory(sharedPoolDir);

        var folderName = FolderNameHelper.GetCleanName(mod.FolderName);
        var sharedModDir = Path.Combine(sharedPoolDir, folderName);

        // Find source profile (the first target that actually has this mod)
        string? sourceProfileDir = null;
        foreach (var profile in targetProfiles)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profile, modsRoot);
            if (profileDir == null) continue;

            var modDir = Path.Combine(profileDir, mod.FolderName);
            var modDirDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(mod.FolderName));

            if (Directory.Exists(modDir) && !IsJunction(modDir))
            {
                sourceProfileDir = profileDir;
                break;
            }
            if (Directory.Exists(modDirDisabled) && !IsJunction(modDirDisabled))
            {
                sourceProfileDir = profileDir;
                mod.FolderName = FolderNameHelper.ToDisabled(mod.FolderName);
                break;
            }
        }

        if (sourceProfileDir == null)
            throw new InvalidOperationException("Could not find the mod in any of the selected profiles.");

        // Move mod from source profile to shared pool
        var sourceModPath = Path.Combine(sourceProfileDir, mod.FolderName);
        if (!Directory.Exists(sharedModDir))
            Directory.Move(sourceModPath, sharedModDir);

        // Save configs and create junctions for all target profiles
        var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);

        foreach (var profile in targetProfiles)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profile, modsRoot);
            if (profileDir == null) continue;

            // Save this profile's config.json(s) before removing the mod copy
            SaveConfigsForProfileMod(sharedModDir, mod, profile, profileDir, configsDir);

            // Remove the profile's copy (if it exists and isn't already handled)
            var modCopyEnabled = Path.Combine(profileDir, FolderNameHelper.ToEnabled(mod.FolderName));
            var modCopyDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(mod.FolderName));

            if (Directory.Exists(modCopyEnabled) && !IsJunction(modCopyEnabled))
                Directory.Delete(modCopyEnabled, true);
            if (Directory.Exists(modCopyDisabled) && !IsJunction(modCopyDisabled))
                Directory.Delete(modCopyDisabled, true);

            // Create junction (always use the clean name; enable/disable is per-profile via rename)
            var junctionPath = Path.Combine(profileDir, folderName);
            if (!Directory.Exists(junctionPath))
                CreateJunction(junctionPath, sharedModDir);
        }

        // Restore active profile's config into the shared folder
        if (config.ActiveProfileName != null && targetProfiles.Contains(config.ActiveProfileName, StringComparer.OrdinalIgnoreCase))
        {
            RestoreConfigsForProfileMod(sharedModDir, folderName, config.ActiveProfileName, configsDir);
        }

        // Track in config
        var info = new SharedModInfo
        {
            SharedFolderName = folderName,
            UniqueID = mod.UniqueID,
            Version = mod.Version,
            ProfileNames = new List<string>(targetProfiles)
        };
        config.SharedMods[folderName] = info;
    }

    public void ShareCollection(ModEntry collection, List<string> targetProfiles, string modsRoot, AppConfig config)
    {
        if (targetProfiles.Count < 2)
            throw new InvalidOperationException("Must select at least 2 profiles to share a collection.");

        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        if (!Directory.Exists(sharedPoolDir))
            Directory.CreateDirectory(sharedPoolDir);

        var folderName = FolderNameHelper.GetCleanName(collection.FolderName);

        var sourceFingerprint = FileSystemHelpers.CalculateCollectionFingerprint(collection.FolderPath);

        // Verify all target profiles have identical collection
        foreach (var profile in targetProfiles)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profile, modsRoot);
            if (profileDir == null)
                throw new InvalidOperationException($"Profile '{profile}' directory not found.");

            var profileCollectionPath = Path.Combine(profileDir, collection.FolderName);
            if (!Directory.Exists(profileCollectionPath))
                throw new InvalidOperationException($"Collection '{collection.FolderName}' not found in profile '{profile}'.");

            if (IsJunction(profileCollectionPath))
                throw new InvalidOperationException($"Collection in '{profile}' is already a junction.");

            var targetFingerprint = FileSystemHelpers.CalculateCollectionFingerprint(profileCollectionPath);

            if (!FileSystemHelpers.CompareCollectionIdentities(sourceFingerprint, targetFingerprint, out var diffs))
            {
                throw new InvalidOperationException(
                    $"Collection in '{profile}' differs from source:\n{string.Join("\n", diffs)}");
            }
        }

        var sharedCollectionDir = Path.Combine(sharedPoolDir, folderName);
        if (!Directory.Exists(sharedCollectionDir))
            Directory.Move(collection.FolderPath, sharedCollectionDir);

        var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);

        foreach (var profile in targetProfiles)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profile, modsRoot)!;

            SaveConfigsForCollectionMods(sharedCollectionDir, folderName, profile, profileDir, configsDir);

            var collectionCopyEnabled = Path.Combine(profileDir, FolderNameHelper.ToEnabled(collection.FolderName));
            var collectionCopyDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(collection.FolderName));

            if (Directory.Exists(collectionCopyEnabled) && !IsJunction(collectionCopyEnabled))
                Directory.Delete(collectionCopyEnabled, true);
            if (Directory.Exists(collectionCopyDisabled) && !IsJunction(collectionCopyDisabled))
                Directory.Delete(collectionCopyDisabled, true);

            var junctionPath = Path.Combine(profileDir, folderName);
            if (!Directory.Exists(junctionPath))
                CreateJunction(junctionPath, sharedCollectionDir);
        }

        if (config.ActiveProfileName != null && targetProfiles.Contains(config.ActiveProfileName, StringComparer.OrdinalIgnoreCase))
        {
            RestoreConfigsForCollectionMods(sharedCollectionDir, folderName, config.ActiveProfileName, configsDir);
        }

        var info = new SharedCollectionInfo
        {
            SharedFolderName = folderName,
            CollectionUniqueID = $"collection:{folderName}",
            ProfileNames = new List<string>(targetProfiles),
            SubModFingerprints = sourceFingerprint
        };
        config.SharedCollections[folderName] = info;
    }

    public void UnshareCollection(string sharedFolderName, string profileName, string modsRoot, AppConfig config)
    {
        if (!config.SharedCollections.TryGetValue(sharedFolderName, out var info)) return;

        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var sharedCollectionDir = Path.Combine(sharedPoolDir, sharedFolderName);
        var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);

        if (profileDir != null)
        {
            var wasDisabled = RemoveJunctionBothStates(profileDir, sharedFolderName);

            if (Directory.Exists(sharedCollectionDir))
            {
                var destName = wasDisabled ? FolderNameHelper.ToDisabled(sharedFolderName) : sharedFolderName;
                var destPath = Path.Combine(profileDir, destName);
                FileSystemHelpers.CopyDirectory(sharedCollectionDir, destPath);

                var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);
                RestoreConfigsForCollectionMods(destPath, sharedFolderName, profileName, configsDir);
            }

            var profileConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName, profileName, sharedFolderName);
            if (Directory.Exists(profileConfigDir))
                Directory.Delete(profileConfigDir, true);
        }

        info.ProfileNames.RemoveAll(n => string.Equals(n, profileName, StringComparison.OrdinalIgnoreCase));

        if (info.ProfileNames.Count <= 1)
        {
            if (info.ProfileNames.Count == 1)
                UnshareCollection(sharedFolderName, info.ProfileNames[0], modsRoot, config);

            if (Directory.Exists(sharedCollectionDir))
                Directory.Delete(sharedCollectionDir, true);

            var modConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);
            if (Directory.Exists(modConfigDir))
            {
                foreach (var profileCfgDir in Directory.GetDirectories(modConfigDir))
                {
                    var collectionCfg = Path.Combine(profileCfgDir, sharedFolderName);
                    if (Directory.Exists(collectionCfg))
                        Directory.Delete(collectionCfg, true);
                }
            }

            config.SharedCollections.Remove(sharedFolderName);
        }
    }

    public void UnshareMod(string sharedFolderName, string profileName, string modsRoot, AppConfig config)
    {
        if (!config.SharedMods.TryGetValue(sharedFolderName, out var info)) return;

        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var sharedModDir = Path.Combine(sharedPoolDir, sharedFolderName);
        var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);

        if (profileDir != null)
        {
            var wasDisabled = RemoveJunctionBothStates(profileDir, sharedFolderName);

            if (Directory.Exists(sharedModDir))
            {
                var destName = wasDisabled ? FolderNameHelper.ToDisabled(sharedFolderName) : sharedFolderName;
                var destPath = Path.Combine(profileDir, destName);
                FileSystemHelpers.CopyDirectory(sharedModDir, destPath);

                var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);
                RestoreConfigsForProfileMod(destPath, sharedFolderName, profileName, configsDir);
            }

            var profileConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName, profileName, sharedFolderName);
            if (Directory.Exists(profileConfigDir))
                Directory.Delete(profileConfigDir, true);
        }

        info.ProfileNames.RemoveAll(n => string.Equals(n, profileName, StringComparison.OrdinalIgnoreCase));

        if (info.ProfileNames.Count <= 1)
        {
            if (info.ProfileNames.Count == 1)
                UnshareMod(sharedFolderName, info.ProfileNames[0], modsRoot, config);

            if (Directory.Exists(sharedModDir))
                Directory.Delete(sharedModDir, true);

            var modConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);
            if (Directory.Exists(modConfigDir))
            {
                foreach (var profileCfgDir in Directory.GetDirectories(modConfigDir))
                {
                    var modCfg = Path.Combine(profileCfgDir, sharedFolderName);
                    if (Directory.Exists(modCfg))
                        Directory.Delete(modCfg, true);
                }
            }

            config.SharedMods.Remove(sharedFolderName);
        }
    }

    public void UnshareModForAllProfiles(string sharedFolderName, string modsRoot, AppConfig config)
    {
        if (!config.SharedMods.TryGetValue(sharedFolderName, out var info)) return;

        var profiles = new List<string>(info.ProfileNames);
        foreach (var profile in profiles)
            UnshareMod(sharedFolderName, profile, modsRoot, config);
    }

    public void SaveSharedModConfigs(string profileName, string modsRoot, AppConfig config)
    {
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);

        foreach (var (folderName, info) in config.SharedMods)
        {
            if (!info.ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
                continue;

            var sharedModDir = Path.Combine(sharedPoolDir, folderName);
            if (!Directory.Exists(sharedModDir)) continue;

            var profileConfigDir = Path.Combine(configsDir, profileName, folderName);
            if (!Directory.Exists(profileConfigDir))
                Directory.CreateDirectory(profileConfigDir);

            // Check if this is a collection (contains sub-mod folders)
            if (FileSystemHelpers.IsCollectionFolder(sharedModDir))
            {
                foreach (var subModDir in Directory.GetDirectories(sharedModDir))
                {
                    var subFolderName = Path.GetFileName(subModDir);
                    if (subFolderName.StartsWith(".")) continue;

                    var configPath = Path.Combine(subModDir, "config.json");
                    if (File.Exists(configPath))
                    {
                        var destDir = Path.Combine(profileConfigDir, subFolderName);
                        if (!Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);
                        File.Copy(configPath, Path.Combine(destDir, "config.json"), true);
                    }
                }
            }
            else
            {
                var configPath = Path.Combine(sharedModDir, "config.json");
                if (File.Exists(configPath))
                    File.Copy(configPath, Path.Combine(profileConfigDir, "config.json"), true);
            }
        }

        foreach (var (folderName, info) in config.SharedCollections)
        {
            if (!info.ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
                continue;

            var sharedCollectionDir = Path.Combine(sharedPoolDir, folderName);
            if (!Directory.Exists(sharedCollectionDir)) continue;

            var profileConfigDir = Path.Combine(configsDir, profileName, folderName);
            if (!Directory.Exists(profileConfigDir))
                Directory.CreateDirectory(profileConfigDir);

            foreach (var subModDir in Directory.GetDirectories(sharedCollectionDir))
            {
                var subFolderName = Path.GetFileName(subModDir);
                if (subFolderName.StartsWith(".")) continue;

                var configPath = Path.Combine(subModDir, "config.json");
                if (File.Exists(configPath))
                {
                    var destDir = Path.Combine(profileConfigDir, subFolderName);
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(configPath, Path.Combine(destDir, "config.json"), true);
                }
            }
        }
    }

    public void RestoreSharedModConfigs(string profileName, string modsRoot, AppConfig config)
    {
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);

        foreach (var (folderName, info) in config.SharedMods)
        {
            if (!info.ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
                continue;

            var sharedModDir = Path.Combine(sharedPoolDir, folderName);
            if (!Directory.Exists(sharedModDir)) continue;

            var profileConfigDir = Path.Combine(configsDir, profileName, folderName);
            if (!Directory.Exists(profileConfigDir)) continue;

            if (FileSystemHelpers.IsCollectionFolder(sharedModDir))
            {
                foreach (var subConfigDir in Directory.GetDirectories(profileConfigDir))
                {
                    var subFolderName = Path.GetFileName(subConfigDir);
                    var configSrc = Path.Combine(subConfigDir, "config.json");
                    if (File.Exists(configSrc))
                    {
                        var destPath = Path.Combine(sharedModDir, subFolderName, "config.json");
                        File.Copy(configSrc, destPath, true);
                    }
                }
            }
            else
            {
                var configSrc = Path.Combine(profileConfigDir, "config.json");
                if (File.Exists(configSrc))
                    File.Copy(configSrc, Path.Combine(sharedModDir, "config.json"), true);
            }
        }

        foreach (var (folderName, info) in config.SharedCollections)
        {
            if (!info.ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
                continue;

            var sharedCollectionDir = Path.Combine(sharedPoolDir, folderName);
            if (!Directory.Exists(sharedCollectionDir)) continue;

            var profileConfigDir = Path.Combine(configsDir, profileName, folderName);
            if (!Directory.Exists(profileConfigDir)) continue;

            foreach (var subConfigDir in Directory.GetDirectories(profileConfigDir))
            {
                var subFolderName = Path.GetFileName(subConfigDir);
                var configSrc = Path.Combine(subConfigDir, "config.json");
                if (File.Exists(configSrc))
                {
                    var destPath = Path.Combine(sharedCollectionDir, subFolderName, "config.json");
                    File.Copy(configSrc, destPath, true);
                }
            }
        }
    }

    public Dictionary<string, List<(string ProfileName, ModEntry Mod)>> DetectDuplicateMods(
        List<string> profileNames, string modsRoot, AppConfig config)
    {
        var modsByUniqueId = new Dictionary<string, List<(string ProfileName, ModEntry Mod)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var profileName in profileNames)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);
            if (profileDir == null) continue;

            foreach (var dir in Directory.GetDirectories(profileDir))
            {
                if (IsJunction(dir)) continue;

                var folderName = Path.GetFileName(dir);
                var manifest = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifest)) continue;

                var entry = FileSystemHelpers.ReadManifestBasic(manifest);
                if (entry == null || string.IsNullOrEmpty(entry.UniqueID)) continue;

                entry.FolderName = folderName;
                entry.FolderPath = dir;

                if (!modsByUniqueId.ContainsKey(entry.UniqueID))
                    modsByUniqueId[entry.UniqueID] = new();

                modsByUniqueId[entry.UniqueID].Add((profileName, entry));
            }
        }

        var duplicates = new Dictionary<string, List<(string ProfileName, ModEntry Mod)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (uid, entries) in modsByUniqueId)
        {
            if (entries.Count >= 2)
                duplicates[uid] = entries;
        }

        return duplicates;
    }

    public async Task<List<DuplicateCollectionGroup>> DetectDuplicateCollections(string modsRoot, AppConfig config)
    {
        var collectionsByName = new Dictionary<string, List<(string profile, string path, Dictionary<string, SubModFingerprint> fingerprint)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileName in config.ProfileNames)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);
            if (profileDir == null) continue;

            if (config.ProfileCollectionNames.TryGetValue(profileName, out var collectionNames))
            {
                foreach (var collectionName in collectionNames)
                {
                    var collectionPath = Path.Combine(profileDir, collectionName);
                    if (!Directory.Exists(collectionPath)) continue;

                    if (IsJunction(collectionPath))
                        continue;

                    if (Path.GetFileName(collectionPath).StartsWith("."))
                        continue;

                    var fingerprint = await Task.Run(() => FileSystemHelpers.CalculateCollectionFingerprint(collectionPath));

                    if (!collectionsByName.ContainsKey(collectionName))
                        collectionsByName[collectionName] = new();

                    collectionsByName[collectionName].Add((profileName, collectionPath, fingerprint));
                }
            }
        }

        var duplicates = new List<DuplicateCollectionGroup>();

        foreach (var (name, instances) in collectionsByName.Where(x => x.Value.Count > 1))
        {
            var firstFingerprint = instances[0].fingerprint;
            var hasIdentityMismatch = instances.Skip(1).Any(x =>
                !FileSystemHelpers.CompareCollectionIdentities(firstFingerprint, x.fingerprint, out _));

            duplicates.Add(new DuplicateCollectionGroup
            {
                CollectionName = name,
                HasIdentityMismatch = hasIdentityMismatch,
                Instances = new System.Collections.ObjectModel.ObservableCollection<DuplicateCollectionInstance>(
                    instances.Select(x => new DuplicateCollectionInstance
                    {
                        ProfileName = x.profile,
                        SubModCount = x.fingerprint.Count
                    }))
            });
        }

        return duplicates;
    }

    public List<string> ValidateSharedPool(string modsRoot, AppConfig config)
    {
        var brokenEntries = new List<string>();
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);

        foreach (var (folderName, info) in config.SharedMods)
        {
            var sharedModDir = Path.Combine(sharedPoolDir, folderName);

            if (!Directory.Exists(sharedModDir))
            {
                brokenEntries.Add(folderName);
                continue;
            }

            foreach (var profileName in info.ProfileNames)
            {
                var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);
                if (profileDir == null) continue;

                var junctionEnabled = Path.Combine(profileDir, folderName);
                var junctionDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(folderName));

                var hasValidJunction =
                    (Directory.Exists(junctionEnabled) && IsJunction(junctionEnabled)) ||
                    (Directory.Exists(junctionDisabled) && IsJunction(junctionDisabled));

                if (!hasValidJunction)
                    brokenEntries.Add(folderName);
            }
        }

        return brokenEntries.Distinct().ToList();
    }

    public List<string> ValidateSharedCollections(string modsRoot, AppConfig config)
    {
        var changedCollections = new List<string>();
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);

        foreach (var (folderName, info) in config.SharedCollections)
        {
            var sharedCollectionDir = Path.Combine(sharedPoolDir, folderName);
            if (!Directory.Exists(sharedCollectionDir)) continue;

            var currentFingerprint = FileSystemHelpers.CalculateCollectionFingerprint(sharedCollectionDir);

            if (!FileSystemHelpers.CompareCollectionIdentities(info.SubModFingerprints, currentFingerprint, out _))
            {
                changedCollections.Add(folderName);
            }
        }

        return changedCollections;
    }

    public void RepairBrokenSharedMod(string sharedFolderName, string modsRoot, AppConfig config)
    {
        if (!config.SharedMods.TryGetValue(sharedFolderName, out var info)) return;

        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var sharedModDir = Path.Combine(sharedPoolDir, sharedFolderName);

        if (!Directory.Exists(sharedModDir))
        {
            config.SharedMods.Remove(sharedFolderName);
            return;
        }

        foreach (var profileName in info.ProfileNames.ToList())
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);
            if (profileDir == null)
            {
                info.ProfileNames.Remove(profileName);
                continue;
            }

            var junctionEnabled = Path.Combine(profileDir, sharedFolderName);
            var junctionDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(sharedFolderName));

            if (Directory.Exists(junctionEnabled) || Directory.Exists(junctionDisabled))
                continue;

            CreateJunction(junctionEnabled, sharedModDir);
        }

        if (info.ProfileNames.Count <= 1)
        {
            UnshareModForAllProfiles(sharedFolderName, modsRoot, config);
        }
    }

    public void CleanupProfileSharedMods(string profileName, string modsRoot, AppConfig config)
    {
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);

        var sharedModsForProfile = config.SharedMods
            .Where(kv => kv.Value.ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var folderName in sharedModsForProfile)
        {
            var profileDir = FileSystemHelpers.FindProfileDir(profileName, modsRoot);
            if (profileDir != null)
            {
                var junctionEnabled = Path.Combine(profileDir, folderName);
                var junctionDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(folderName));

                if (Directory.Exists(junctionEnabled) && IsJunction(junctionEnabled))
                    RemoveJunction(junctionEnabled);
                if (Directory.Exists(junctionDisabled) && IsJunction(junctionDisabled))
                    RemoveJunction(junctionDisabled);
            }

            var info = config.SharedMods[folderName];
            info.ProfileNames.RemoveAll(n => string.Equals(n, profileName, StringComparison.OrdinalIgnoreCase));

            var profileConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName, profileName);
            if (Directory.Exists(profileConfigDir))
                Directory.Delete(profileConfigDir, true);

            if (info.ProfileNames.Count <= 1)
            {
                if (info.ProfileNames.Count == 1)
                {
                    var lastProfile = info.ProfileNames[0];
                    var lastProfileDir = FileSystemHelpers.FindProfileDir(lastProfile, modsRoot);
                    if (lastProfileDir != null)
                    {
                        var wasDisabled = RemoveJunctionBothStates(lastProfileDir, folderName);

                        var sharedModDir = Path.Combine(sharedPoolDir, folderName);
                        if (Directory.Exists(sharedModDir))
                        {
                            var destName = wasDisabled ? FolderNameHelper.ToDisabled(folderName) : folderName;
                            var destPath = Path.Combine(lastProfileDir, destName);
                            if (!Directory.Exists(destPath))
                                FileSystemHelpers.CopyDirectory(sharedModDir, destPath);

                            var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);
                            RestoreConfigsForProfileMod(destPath, folderName, lastProfile, configsDir);

                            Directory.Delete(sharedModDir, true);
                        }

                        var lastConfigDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName, lastProfile, folderName);
                        if (Directory.Exists(lastConfigDir))
                            Directory.Delete(lastConfigDir, true);
                    }
                }

                config.SharedMods.Remove(folderName);
            }
        }
    }

    public void RenameProfileSharedMods(string oldName, string newName, string modsRoot, AppConfig config)
    {
        var sharedPoolDir = Path.Combine(modsRoot, ISharedModService.SharedPoolFolderName);
        var configsDir = Path.Combine(sharedPoolDir, ISharedModService.ConfigStoreFolderName);

        var oldConfigDir = Path.Combine(configsDir, oldName);
        var newConfigDir = Path.Combine(configsDir, newName);
        if (Directory.Exists(oldConfigDir) && !Directory.Exists(newConfigDir))
            Directory.Move(oldConfigDir, newConfigDir);

        foreach (var (_, info) in config.SharedMods)
        {
            var idx = info.ProfileNames.FindIndex(n =>
                string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                info.ProfileNames[idx] = newName;
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Removes a junction in both enabled and disabled states, returns whether it was disabled.
    /// </summary>
    private bool RemoveJunctionBothStates(string profileDir, string folderName)
    {
        var junctionEnabled = Path.Combine(profileDir, folderName);
        var junctionDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(folderName));

        if (Directory.Exists(junctionDisabled) && IsJunction(junctionDisabled))
        {
            RemoveJunction(junctionDisabled);
            return true; // was disabled
        }

        if (Directory.Exists(junctionEnabled) && IsJunction(junctionEnabled))
        {
            RemoveJunction(junctionEnabled);
            return false; // was enabled
        }

        return false;
    }

    private void SaveConfigsForProfileMod(string sharedModDir, ModEntry mod, string profileName,
        string profileDir, string configsDir)
    {
        var cleanName = FolderNameHelper.GetCleanName(mod.FolderName);
        var profileConfigDir = Path.Combine(configsDir, profileName, cleanName);
        if (!Directory.Exists(profileConfigDir))
            Directory.CreateDirectory(profileConfigDir);

        var modPathEnabled = Path.Combine(profileDir, cleanName);
        var modPathDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(cleanName));
        var actualModPath = Directory.Exists(modPathEnabled) ? modPathEnabled :
            Directory.Exists(modPathDisabled) ? modPathDisabled : null;

        if (actualModPath == null || IsJunction(actualModPath)) return;

        if (mod.IsCollection || FileSystemHelpers.IsCollectionFolder(actualModPath))
        {
            foreach (var subDir in Directory.GetDirectories(actualModPath))
            {
                var subName = Path.GetFileName(subDir);
                var configPath = Path.Combine(subDir, "config.json");
                if (File.Exists(configPath))
                {
                    var destDir = Path.Combine(profileConfigDir, subName);
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(configPath, Path.Combine(destDir, "config.json"), true);
                }
            }
        }
        else
        {
            var configPath = Path.Combine(actualModPath, "config.json");
            if (File.Exists(configPath))
                File.Copy(configPath, Path.Combine(profileConfigDir, "config.json"), true);
        }
    }

    private static void RestoreConfigsForProfileMod(string targetModDir, string sharedFolderName,
        string profileName, string configsDir)
    {
        var profileConfigDir = Path.Combine(configsDir, profileName, sharedFolderName);
        if (!Directory.Exists(profileConfigDir)) return;

        // Check if direct config.json exists (non-collection)
        var directConfig = Path.Combine(profileConfigDir, "config.json");
        if (File.Exists(directConfig))
        {
            File.Copy(directConfig, Path.Combine(targetModDir, "config.json"), true);
            return;
        }

        // Collection: iterate sub-folders
        foreach (var subConfigDir in Directory.GetDirectories(profileConfigDir))
        {
            var subName = Path.GetFileName(subConfigDir);
            var configSrc = Path.Combine(subConfigDir, "config.json");
            if (File.Exists(configSrc))
            {
                var destPath = Path.Combine(targetModDir, subName, "config.json");
                if (Directory.Exists(Path.Combine(targetModDir, subName)))
                    File.Copy(configSrc, destPath, true);
            }
        }
    }

    private void SaveConfigsForCollectionMods(string sharedCollectionDir, string collectionName,
        string profileName, string profileDir, string configsDir)
    {
        var profileConfigDir = Path.Combine(configsDir, profileName, collectionName);
        if (!Directory.Exists(profileConfigDir))
            Directory.CreateDirectory(profileConfigDir);

        var collectionPathEnabled = Path.Combine(profileDir, collectionName);
        var collectionPathDisabled = Path.Combine(profileDir, FolderNameHelper.ToDisabled(collectionName));
        var actualCollectionPath = Directory.Exists(collectionPathEnabled) ? collectionPathEnabled :
            Directory.Exists(collectionPathDisabled) ? collectionPathDisabled : null;

        if (actualCollectionPath == null || IsJunction(actualCollectionPath)) return;

        foreach (var subDir in Directory.GetDirectories(actualCollectionPath))
        {
            var subName = Path.GetFileName(subDir);
            var configPath = Path.Combine(subDir, "config.json");
            if (File.Exists(configPath))
            {
                var destDir = Path.Combine(profileConfigDir, subName);
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(configPath, Path.Combine(destDir, "config.json"), true);
            }
        }
    }

    private static void RestoreConfigsForCollectionMods(string sharedCollectionDir, string collectionName,
        string profileName, string configsDir)
    {
        var profileConfigDir = Path.Combine(configsDir, profileName, collectionName);
        if (!Directory.Exists(profileConfigDir)) return;

        foreach (var subConfigDir in Directory.GetDirectories(profileConfigDir))
        {
            var subName = Path.GetFileName(subConfigDir);
            var configSrc = Path.Combine(subConfigDir, "config.json");
            if (File.Exists(configSrc))
            {
                var destPath = Path.Combine(sharedCollectionDir, subName, "config.json");
                if (Directory.Exists(Path.Combine(sharedCollectionDir, subName)))
                    File.Copy(configSrc, destPath, true);
            }
        }
    }
}
