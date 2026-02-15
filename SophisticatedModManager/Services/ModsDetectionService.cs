using System.IO;

namespace SophisticatedModManager.Services;

public class ModsDetectionService : IModsDetectionService
{
    public DetectionResult DetectExistingLayout(string modsRootPath, string savesPath)
    {
        var result = new DetectionResult();

        if (!Directory.Exists(modsRootPath))
        {
            result.Scenario = DetectionScenario.NoModsFolder;
            return result;
        }

        var allDirs = Directory.GetDirectories(modsRootPath);
        var dotPrefixed = new List<DetectedFolder>();
        var multiModFolders = new List<DetectedFolder>();
        bool hasLooseMods = false;

        foreach (var dir in allDirs)
        {
            var folderName = Path.GetFileName(dir);

            if (folderName.Equals(ISharedModService.SharedPoolFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (FolderNameHelper.IsDisabled(folderName))
            {
                var detected = AnalyzeDotPrefixedFolder(dir, folderName);
                detected.IsDotPrefixed = true;
                dotPrefixed.Add(detected);

                if (detected.IsLikelyProfile)
                    multiModFolders.Add(detected);
            }
            else
            {
                if (File.Exists(Path.Combine(dir, "manifest.json")))
                {
                    hasLooseMods = true;
                }
                else
                {
                    int modCount = CountModsRecursive(dir, maxDepth: 2);

                    if (modCount > 0)
                    {
                        multiModFolders.Add(new DetectedFolder
                        {
                            FolderName = folderName,
                            FullPath = dir,
                            IsLikelyProfile = true,
                            SubModCount = modCount,
                            IsDotPrefixed = false
                        });
                    }
                }
            }
        }

        result.DotPrefixedFolders = dotPrefixed;
        result.MultiModFolders = multiModFolders;
        result.HasLooseModsAtRoot = hasLooseMods;

        if (multiModFolders.Count == 0)
        {
            result.Scenario = hasLooseMods || allDirs.Length > 0
                ? DetectionScenario.ModsExistNoProfiles
                : DetectionScenario.NoModsFolder;
            return result;
        }

        result.DetectedProfileNames = multiModFolders
            .Select(p => ProfileService.ParseProfileName(p.FolderName) ?? FolderNameHelper.GetCleanName(p.FolderName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Directory.Exists(savesPath))
        {
            var saveDirs = Directory.GetDirectories(savesPath);
            var matchedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var saveDir in saveDirs)
            {
                var saveName = Path.GetFileName(saveDir);
                if (saveName.StartsWith(".") && saveName.EndsWith("Saves"))
                {
                    var profileName = saveName[1..^5];
                    if (result.DetectedProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
                        matchedProfiles.Add(profileName);
                }
            }

            var activeSaves = Path.Combine(savesPath, "Saves");
            if (Directory.Exists(activeSaves))
            {
                result.SaveFolderNames = Directory.GetDirectories(activeSaves)
                    .Select(Path.GetFileName)
                    .Where(n => n != null)
                    .Select(n => n!)
                    .ToList();

                if (matchedProfiles.Count == result.DetectedProfileNames.Count - 1
                    && result.SaveFolderNames.Count > 0)
                {
                    var unmatchedProfile = result.DetectedProfileNames
                        .FirstOrDefault(p => !matchedProfiles.Contains(p));
                    if (unmatchedProfile != null)
                        matchedProfiles.Add(unmatchedProfile);
                }
            }

            if (matchedProfiles.Count == result.DetectedProfileNames.Count)
                result.Scenario = DetectionScenario.ProfilesAndSaves;
            else
                result.Scenario = DetectionScenario.ProfilesDetected;
        }
        else
        {
            result.Scenario = DetectionScenario.ProfilesDetected;
        }

        return result;
    }

    private static DetectedFolder AnalyzeDotPrefixedFolder(string path, string folderName)
    {
        var detected = new DetectedFolder
        {
            FolderName = folderName,
            FullPath = path
        };

        if (File.Exists(Path.Combine(path, "manifest.json")))
        {
            detected.IsLikelyDisabledMod = true;
            detected.IsLikelyProfile = false;
            return detected;
        }

        int modCount = CountModsRecursive(path, maxDepth: 2);

        detected.SubModCount = modCount;
        detected.IsLikelyProfile = modCount > 0;
        detected.IsLikelyDisabledMod = false;

        return detected;
    }

    private static int CountModsRecursive(string path, int maxDepth)
    {
        if (maxDepth <= 0) return 0;
        int count = 0;
        foreach (var subDir in Directory.GetDirectories(path))
        {
            if (File.Exists(Path.Combine(subDir, "manifest.json")))
                count++;
            else
                count += CountModsRecursive(subDir, maxDepth - 1);
        }
        return count;
    }
}
