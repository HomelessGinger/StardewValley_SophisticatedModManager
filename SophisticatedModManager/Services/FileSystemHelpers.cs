using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

/// <summary>
/// Static utility methods for common file system operations (manifest parsing, directory copying, etc.).
/// Consolidates duplicate code previously scattered across services and ViewModels.
/// </summary>
public static class FileSystemHelpers
{
    /// <summary>
    /// Reads a mod manifest.json file and returns a full ModEntry with all properties.
    /// </summary>
    /// <param name="manifestPath">Path to manifest.json file</param>
    /// <returns>ModEntry with Name, Author, Version, Description, UniqueID, UpdateKeys, NexusModId; null if parsing fails</returns>
    public static ModEntry? ReadManifest(string manifestPath) => ReadManifestCore(manifestPath, true);

    /// <summary>
    /// Reads a mod manifest.json file and returns a minimal ModEntry with only Name, Version, and UniqueID.
    /// </summary>
    /// <param name="manifestPath">Path to manifest.json file</param>
    /// <returns>ModEntry with Name, Version, UniqueID; null if parsing fails</returns>
    public static ModEntry? ReadManifestBasic(string manifestPath) => ReadManifestCore(manifestPath, false);

    /// <summary>
    /// Core manifest reading logic shared by ReadManifest and ReadManifestBasic.
    /// </summary>
    private static ModEntry? ReadManifestCore(string manifestPath, bool fullParse)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            var root = doc.RootElement;

            var entry = new ModEntry
            {
                Name = root.TryGetProperty("Name", out var name) ? name.GetString() ?? "Unknown" : "Unknown",
                Version = root.TryGetProperty("Version", out var version) ? version.GetString() ?? "" : "",
                UniqueID = root.TryGetProperty("UniqueID", out var uid) ? uid.GetString() ?? "" : ""
            };

            if (fullParse)
            {
                entry.Author = root.TryGetProperty("Author", out var author) ? author.GetString() ?? "" : "";
                entry.Description = root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "";

                var updateKeys = new List<string>();
                if (root.TryGetProperty("UpdateKeys", out var keysElement) && keysElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var key in keysElement.EnumerateArray())
                    {
                        var keyStr = key.GetString();
                        if (keyStr != null) updateKeys.Add(keyStr);
                    }
                }
                entry.UpdateKeys = updateKeys;
                entry.NexusModId = ParseNexusModId(updateKeys);
            }

            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the directory path for a profile, checking both active and inactive (dot-prefixed) variants.
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <param name="modsRoot">Root Mods directory</param>
    /// <returns>Full path to the profile directory, or null if not found</returns>
    public static string? FindProfileDir(string profileName, string modsRoot)
    {
        var active = ProfileService.GetActiveProfileModPath(profileName, modsRoot);
        if (Directory.Exists(active)) return active;

        var inactive = ProfileService.GetInactiveProfileModPath(profileName, modsRoot);
        if (Directory.Exists(inactive)) return inactive;

        return null;
    }

    /// <summary>
    /// Recursively copies a directory and all its contents to a new location.
    /// </summary>
    /// <param name="sourceDir">Source directory path</param>
    /// <param name="destDir">Destination directory path</param>
    /// <param name="overwrite">If true, overwrites existing files; if false, skips existing files</param>
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = false)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite);

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destDir, dirName), overwrite);
        }
    }

    /// <summary>
    /// Copies multiple mod folders to a target directory with optional overwrite.
    /// </summary>
    /// <param name="modFolders">Collection of source mod folder paths</param>
    /// <param name="targetDir">Target directory where mods will be copied</param>
    /// <param name="overwrite">If true, deletes existing destination folders before copying</param>
    public static void CopyModFolders(IEnumerable<string> modFolders, string targetDir, bool overwrite = true)
    {
        foreach (var modFolder in modFolders)
        {
            var destDir = Path.Combine(targetDir, Path.GetFileName(modFolder));
            if (overwrite && Directory.Exists(destDir))
                Directory.Delete(destDir, true);
            CopyDirectory(modFolder, destDir);
        }
    }

    /// <summary>
    /// Determines if a directory is a collection folder (contains sub-mod folders with manifests, but no manifest itself).
    /// </summary>
    /// <param name="dirPath">Directory path to check</param>
    /// <returns>True if the directory is a collection folder, false otherwise</returns>
    public static bool IsCollectionFolder(string dirPath)
    {
        // A collection has sub-folders with manifests but no manifest itself
        if (File.Exists(Path.Combine(dirPath, "manifest.json")))
            return false;

        return Directory.GetDirectories(dirPath)
            .Any(sub => File.Exists(Path.Combine(sub, "manifest.json")));
    }

    /// <summary>
    /// Extracts a zip/rar/7z archive to a directory.
    /// </summary>
    /// <param name="archivePath">Path to the archive file</param>
    /// <param name="targetDir">Optional target directory; if null, creates a temp directory</param>
    /// <returns>Path to the extraction directory</returns>
    public static string ExtractArchive(string archivePath, string? targetDir = null)
    {
        var extractDir = targetDir ?? Path.Combine(
            Path.GetTempPath(), "SMM_Extract_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(extractDir);

        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext == ".zip")
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else
        {
            using var archive = ArchiveFactory.Open(archivePath);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                entry.WriteToDirectory(extractDir, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }
        }
        return extractDir;
    }

    // --- Private helpers ---

    private static int? ParseNexusModId(List<string> updateKeys)
    {
        foreach (var key in updateKeys)
        {
            if (key.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase))
            {
                var idStr = key[6..].Split('@')[0];
                if (int.TryParse(idStr, out var id)) return id;
            }
        }
        return null;
    }

    // --- Collection fingerprinting methods ---

    /// <summary>
    /// Calculates a fingerprint for a collection by hashing all submod manifests.
    /// </summary>
    /// <param name="collectionPath">Path to the collection directory</param>
    /// <returns>Dictionary mapping submod folder names to their fingerprints</returns>
    public static Dictionary<string, SubModFingerprint> CalculateCollectionFingerprint(string collectionPath)
    {
        var fingerprints = new Dictionary<string, SubModFingerprint>();

        foreach (var subModDir in Directory.GetDirectories(collectionPath))
        {
            var manifestPath = Path.Combine(subModDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var manifest = ReadManifestBasic(manifestPath);
            if (manifest == null) continue;

            var hash = CalculateSHA256(manifestPath);

            fingerprints[Path.GetFileName(subModDir)] = new SubModFingerprint
            {
                UniqueID = manifest.UniqueID,
                Version = manifest.Version,
                ManifestHash = hash
            };
        }

        return fingerprints;
    }

    /// <summary>
    /// Calculates the SHA256 hash of a file.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>Base64-encoded SHA256 hash</returns>
    public static string CalculateSHA256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Compares two collection fingerprints for identity.
    /// </summary>
    /// <param name="a">First fingerprint</param>
    /// <param name="b">Second fingerprint</param>
    /// <param name="differences">Output list of differences found</param>
    /// <returns>True if fingerprints match exactly, false otherwise</returns>
    public static bool CompareCollectionIdentities(
        Dictionary<string, SubModFingerprint> a,
        Dictionary<string, SubModFingerprint> b,
        out List<string> differences)
    {
        differences = new();

        if (a.Count != b.Count)
        {
            differences.Add($"SubMod count mismatch: {a.Count} vs {b.Count}");
            return false;
        }

        foreach (var (folderName, fpA) in a)
        {
            if (!b.TryGetValue(folderName, out var fpB))
            {
                differences.Add($"SubMod '{folderName}' missing in second collection");
                continue;
            }

            if (fpA.UniqueID != fpB.UniqueID)
                differences.Add($"{folderName}: Different mod (UniqueID mismatch)");
            if (fpA.Version != fpB.Version)
                differences.Add($"{folderName}: Version mismatch ({fpA.Version} vs {fpB.Version})");
            if (fpA.ManifestHash != fpB.ManifestHash)
                differences.Add($"{folderName}: Manifest changed");
        }

        return differences.Count == 0;
    }
}
