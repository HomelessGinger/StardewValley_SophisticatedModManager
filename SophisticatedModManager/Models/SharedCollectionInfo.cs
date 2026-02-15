namespace SophisticatedModManager.Models;

public class SharedCollectionInfo
{
    public string SharedFolderName { get; set; } = string.Empty;
    public string CollectionUniqueID { get; set; } = string.Empty;  // "collection:{name}"
    public List<string> ProfileNames { get; set; } = new();
    public Dictionary<string, SubModFingerprint> SubModFingerprints { get; set; } = new();
}

public class SubModFingerprint
{
    public string UniqueID { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;  // SHA256 of manifest.json
}
