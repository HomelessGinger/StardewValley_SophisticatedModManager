namespace SophisticatedModManager.Services;

public class SaveBackupInfo
{
    public string ProfileName { get; set; } = string.Empty;
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public interface ISaveBackupService
{
    List<SaveBackupInfo> BackupProfileSaves(string profileName, string savesPath, string? activeProfileName, int maxBackups);
    List<SaveBackupInfo> BackupAllProfileSaves(List<string> profileNames, string savesPath, string? activeProfileName, int maxBackups);
    List<SaveBackupInfo> GetBackupsForProfile(string profileName, string savesPath);
    void RestoreBackup(SaveBackupInfo backup, string savesPath, string? activeProfileName);
    void RestoreSaves(List<SaveBackupInfo> backups, string savesPath, string? activeProfileName);
    DateTime? GetCurrentSaveTimestamp(string profileName, string savesPath, string? activeProfileName);
}
