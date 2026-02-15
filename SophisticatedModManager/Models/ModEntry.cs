namespace SophisticatedModManager.Models;

public class ModEntry
{
    public string FolderName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UniqueID { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsCommon { get; set; }
    public bool IsCollection { get; set; }
    public List<ModEntry> SubMods { get; set; } = new();
    public List<string> UpdateKeys { get; set; } = new();
    public int? NexusModId { get; set; }
    public bool IsShared { get; set; }
    public string? SharedFolderName { get; set; }
}
