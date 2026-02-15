using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public interface IModConfigService
{
    bool HasConfig(string modFolderPath);
    List<ConfigEntry> LoadConfig(string modFolderPath);
    void SaveConfig(string modFolderPath, List<ConfigEntry> entries);
}
