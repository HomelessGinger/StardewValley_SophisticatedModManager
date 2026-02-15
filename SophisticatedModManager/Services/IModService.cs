using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public interface IModService
{
    List<ModEntry> GetModsForProfile(string profileName, string modsRootPath, List<string> collectionNames);
    List<ModEntry> GetCommonMods(string modsRootPath, List<string> profileNames, List<string> commonCollectionNames);
    void SetModEnabled(ModEntry mod, bool enabled);
    void DeleteMod(ModEntry mod);
    void MoveMod(ModEntry mod, string targetDirectoryPath);
    void DuplicateMod(ModEntry mod, string targetDirectoryPath);
}
