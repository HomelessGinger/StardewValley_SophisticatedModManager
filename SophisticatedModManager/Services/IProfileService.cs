namespace SophisticatedModManager.Services;

public interface IProfileService
{
    void CreateProfile(string name, string modsRootPath, string savesPath);
    void DeleteProfile(string name, string modsRootPath, string savesPath);
    void RenameProfile(string oldName, string newName, string modsRootPath, string savesPath);
    void SwitchProfile(string? fromName, string toName, string modsRootPath, string savesPath);
    void MigrateProfileFolders(List<string> profileNames, string modsRootPath);
    bool IsGameRunning();
}
