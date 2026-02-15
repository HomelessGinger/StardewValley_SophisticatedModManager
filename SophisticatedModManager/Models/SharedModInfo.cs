namespace SophisticatedModManager.Models;

public class SharedModInfo
{
    public string SharedFolderName { get; set; } = string.Empty;
    public string UniqueID { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> ProfileNames { get; set; } = new();
}
