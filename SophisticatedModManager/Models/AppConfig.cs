using System.IO;

namespace SophisticatedModManager.Models;

public class AppConfig
{
    public string SavesPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StardewValley");

    public string GamePath { get; set; } = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";

    public string? ActiveProfileName { get; set; }

    public List<string> ProfileNames { get; set; } = new();

    public bool ShowModDescriptions { get; set; } = true;

    public List<string> CommonCollectionNames { get; set; } = new();

    public Dictionary<string, List<string>> ProfileCollectionNames { get; set; } = new();

    public bool ResetSearchOnProfileSwitch { get; set; }

    public string? NexusApiKey { get; set; }

    public List<string> VanillaProfileNames { get; set; } = new();

    public List<string> SavedCommonModEnabledFolders { get; set; } = new();

    public Dictionary<string, SharedModInfo> SharedMods { get; set; } = new();

    public int MaxSaveBackups { get; set; } = 1;
}
