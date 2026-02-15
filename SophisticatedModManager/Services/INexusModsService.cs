using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public interface INexusModsService
{
    bool IsConfigured { get; }
    bool IsPremium { get; }

    void Configure(string apiKey);

    Task<NexusUserValidation?> ValidateApiKeyAsync(string apiKey);
    Task<NexusModInfo?> GetModInfoAsync(int modId);
    Task<List<NexusModFile>> GetModFilesAsync(int modId);
    Task<string?> GetDownloadLinkAsync(int modId, int fileId, string? nxmKey = null, long? nxmExpires = null);
    Task<string> DownloadFileAsync(string downloadUrl, string targetDir);
    Task<List<NexusModInfo>> GetTrendingModsAsync();
    Task<List<NexusModInfo>> GetLatestAddedModsAsync();
    Task<List<NexusModInfo>> GetLatestUpdatedModsAsync();
    Task<List<int>> GetRecentlyUpdatedModIdsAsync();
    Task<Dictionary<string, List<string>>> GetChangelogAsync(int modId);
    Task<bool> EndorseModAsync(int modId, string modVersion);
    Task<bool> AbstainModAsync(int modId, string modVersion);
    Task<HashSet<int>> GetUserEndorsedModIdsAsync();

    int? ParseNexusModId(List<string> updateKeys);
    NxmUrlInfo? ParseNxmUrl(string nxmUrl);
}

public class NxmUrlInfo
{
    public string GameDomain { get; set; } = string.Empty;
    public int ModId { get; set; }
    public int FileId { get; set; }
    public string? Key { get; set; }
    public long? Expires { get; set; }
}
