namespace SophisticatedModManager.Services;

public interface IModsDetectionService
{
    DetectionResult DetectExistingLayout(string modsRootPath, string savesPath);
}

public enum DetectionScenario
{
    NoModsFolder,
    ModsExistNoProfiles,
    ProfilesDetected,
    ProfilesAndSaves
}

public class DetectedFolder
{
    public string FolderName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsLikelyProfile { get; set; }
    public bool IsLikelyDisabledMod { get; set; }
    public int SubModCount { get; set; }
    public bool IsDotPrefixed { get; set; }
}

public class DetectionResult
{
    public DetectionScenario Scenario { get; set; }
    public List<DetectedFolder> DotPrefixedFolders { get; set; } = new();
    public List<DetectedFolder> MultiModFolders { get; set; } = new();
    public List<string> DetectedProfileNames { get; set; } = new();
    public List<string> SaveFolderNames { get; set; } = new();
    public bool HasLooseModsAtRoot { get; set; }
}
