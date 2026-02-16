using SophisticatedModManager.Models;
using SophisticatedModManager.ViewModels;

namespace SophisticatedModManager.Services;

public interface ISharedModService
{
    const string SharedPoolFolderName = ".[SHARED]";
    const string ConfigStoreFolderName = ".configs";

    bool IsJunction(string path);
    void CreateJunction(string linkPath, string targetPath);
    void RemoveJunction(string linkPath);

    void ShareMod(ModEntry mod, List<string> targetProfiles, string modsRoot, AppConfig config);
    void UnshareMod(string sharedFolderName, string profileName, string modsRoot, AppConfig config);
    void UnshareModForAllProfiles(string sharedFolderName, string modsRoot, AppConfig config);

    void SaveSharedModConfigs(string profileName, string modsRoot, AppConfig config);
    void RestoreSharedModConfigs(string profileName, string modsRoot, AppConfig config);

    void ShareCollection(ModEntry collection, List<string> targetProfiles, string modsRoot, AppConfig config);
    void UnshareCollection(string sharedFolderName, string profileName, string modsRoot, AppConfig config);
    Task<List<DuplicateCollectionGroup>> DetectDuplicateCollections(string modsRoot, AppConfig config);
    List<string> ValidateSharedCollections(string modsRoot, AppConfig config);

    Dictionary<string, List<(string ProfileName, ModEntry Mod)>> DetectDuplicateMods(
        List<string> profileNames, string modsRoot, AppConfig config);

    List<string> ValidateSharedPool(string modsRoot, AppConfig config);
    void RepairBrokenSharedMod(string sharedFolderName, string modsRoot, AppConfig config);

    void CleanupProfileSharedMods(string profileName, string modsRoot, AppConfig config);
    void RenameProfileSharedMods(string oldName, string newName, string modsRoot, AppConfig config);
}
