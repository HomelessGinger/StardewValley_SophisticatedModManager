using System.Globalization;
using System.IO;

namespace SophisticatedModManager.Services;

public class SaveBackupService : ISaveBackupService
{
    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    public List<SaveBackupInfo> BackupProfileSaves(string profileName, string savesPath, string? activeProfileName, int maxBackups)
    {
        var results = new List<SaveBackupInfo>();
        if (maxBackups <= 0) return results;

        var saveDir = GetSaveDir(profileName, savesPath, activeProfileName);
        if (saveDir == null || !Directory.Exists(saveDir)) return results;

        var saveFolders = Directory.GetDirectories(saveDir);
        if (saveFolders.Length == 0) return results;

        var backupsRoot = GetBackupsRoot(profileName, savesPath);
        var timestamp = DateTime.Now;
        var timestampStr = timestamp.ToString(TimestampFormat);

        foreach (var saveFolder in saveFolders)
        {
            var saveName = Path.GetFileName(saveFolder);

            // Check if this individual save changed since its latest backup
            var latestWriteTime = GetLatestWriteTime(saveFolder);
            if (latestWriteTime == null) continue;

            var existingSaveBackups = GetBackupsForSave(backupsRoot, profileName, saveName);
            if (existingSaveBackups.Count > 0 && existingSaveBackups[0].Timestamp >= latestWriteTime.Value)
                continue;

            // Create per-save backup: .{Profile}SaveBackups/{SaveName}/{timestamp}/
            var saveBackupDir = Path.Combine(backupsRoot, saveName, timestampStr);
            Directory.CreateDirectory(saveBackupDir);

            FileSystemHelpers.CopyDirectory(saveFolder, saveBackupDir);

            results.Add(new SaveBackupInfo
            {
                ProfileName = profileName,
                SaveName = saveName,
                Timestamp = timestamp,
                BackupPath = saveBackupDir,
                SizeBytes = GetDirectorySize(saveBackupDir)
            });

            // Prune this save's backups to max limit
            var saveBackupsDir = Path.Combine(backupsRoot, saveName);
            PruneOldBackups(saveBackupsDir, maxBackups);
        }

        return results;
    }

    public List<SaveBackupInfo> BackupAllProfileSaves(List<string> profileNames, string savesPath, string? activeProfileName, int maxBackups)
    {
        var results = new List<SaveBackupInfo>();
        foreach (var name in profileNames)
        {
            results.AddRange(BackupProfileSaves(name, savesPath, activeProfileName, maxBackups));
        }
        return results;
    }

    public List<SaveBackupInfo> GetBackupsForProfile(string profileName, string savesPath)
    {
        var backupsRoot = GetBackupsRoot(profileName, savesPath);
        var results = new List<SaveBackupInfo>();

        if (!Directory.Exists(backupsRoot)) return results;

        // Iterate save-name subdirectories
        foreach (var saveNameDir in Directory.GetDirectories(backupsRoot))
        {
            var saveName = Path.GetFileName(saveNameDir);

            // Iterate timestamp directories within each save
            foreach (var timestampDir in Directory.GetDirectories(saveNameDir))
            {
                var folderName = Path.GetFileName(timestampDir);
                if (DateTime.TryParseExact(folderName, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                {
                    results.Add(new SaveBackupInfo
                    {
                        ProfileName = profileName,
                        SaveName = saveName,
                        Timestamp = timestamp,
                        BackupPath = timestampDir,
                        SizeBytes = GetDirectorySize(timestampDir)
                    });
                }
            }
        }

        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return results;
    }

    public void RestoreBackup(SaveBackupInfo backup, string savesPath, string? activeProfileName)
    {
        var saveDir = GetSaveDir(backup.ProfileName, savesPath, activeProfileName);
        if (saveDir == null) return;

        // Only replace the specific save folder, not all saves
        var targetSaveDir = Path.Combine(saveDir, backup.SaveName);

        if (Directory.Exists(targetSaveDir))
        {
            Directory.Delete(targetSaveDir, true);
        }

        Directory.CreateDirectory(targetSaveDir);
        FileSystemHelpers.CopyDirectory(backup.BackupPath, targetSaveDir, overwrite: true);
    }

    public void RestoreSaves(List<SaveBackupInfo> backups, string savesPath, string? activeProfileName)
    {
        foreach (var backup in backups)
        {
            RestoreBackup(backup, savesPath, activeProfileName);
        }
    }

    public DateTime? GetCurrentSaveTimestamp(string profileName, string savesPath, string? activeProfileName)
    {
        var saveDir = GetSaveDir(profileName, savesPath, activeProfileName);
        if (saveDir == null || !Directory.Exists(saveDir)) return null;

        var saveFolders = Directory.GetDirectories(saveDir);
        if (saveFolders.Length == 0) return null;

        DateTime latest = DateTime.MinValue;
        foreach (var folder in saveFolders)
        {
            var lwt = GetLatestWriteTime(folder);
            if (lwt.HasValue && lwt.Value > latest) latest = lwt.Value;
        }

        return latest == DateTime.MinValue ? null : latest;
    }

    private static DateTime? GetLatestWriteTime(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        DateTime latest = DateTime.MinValue;
        var lwt = Directory.GetLastWriteTime(folder);
        if (lwt > latest) latest = lwt;

        foreach (var file in Directory.GetFiles(folder))
        {
            var fwt = File.GetLastWriteTime(file);
            if (fwt > latest) latest = fwt;
        }

        return latest == DateTime.MinValue ? null : latest;
    }

    private List<SaveBackupInfo> GetBackupsForSave(string backupsRoot, string profileName, string saveName)
    {
        var saveBackupsDir = Path.Combine(backupsRoot, saveName);
        var results = new List<SaveBackupInfo>();

        if (!Directory.Exists(saveBackupsDir)) return results;

        foreach (var dir in Directory.GetDirectories(saveBackupsDir))
        {
            var folderName = Path.GetFileName(dir);
            if (DateTime.TryParseExact(folderName, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                results.Add(new SaveBackupInfo
                {
                    ProfileName = profileName,
                    SaveName = saveName,
                    Timestamp = timestamp,
                    BackupPath = dir,
                    SizeBytes = GetDirectorySize(dir)
                });
            }
        }

        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return results;
    }

    private static string? GetSaveDir(string profileName, string savesPath, string? activeProfileName)
    {
        if (string.Equals(profileName, activeProfileName, StringComparison.OrdinalIgnoreCase))
        {
            // Active profile uses the main Saves folder
            return ProfileService.GetActiveSavesPath(savesPath);
        }
        else
        {
            // Inactive profile uses hidden saves folder
            return ProfileService.GetInactiveSavesPath(profileName, savesPath);
        }
    }

    private static string GetBackupsRoot(string profileName, string savesPath)
    {
        return Path.Combine(savesPath, "." + profileName + "SaveBackups");
    }

    private static void PruneOldBackups(string backupsDir, int maxBackups)
    {
        if (!Directory.Exists(backupsDir)) return;

        var dirs = Directory.GetDirectories(backupsDir)
            .OrderByDescending(d => Path.GetFileName(d))
            .ToList();

        while (dirs.Count > maxBackups)
        {
            var oldest = dirs[^1];
            try
            {
                Directory.Delete(oldest, true);
            }
            catch
            {
                // best effort
            }
            dirs.RemoveAt(dirs.Count - 1);
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                size += new FileInfo(file).Length;
        }
        catch
        {
            // best effort
        }
        return size;
    }
}
