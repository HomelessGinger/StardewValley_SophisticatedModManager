using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Web;
using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public class NexusModsService : INexusModsService
{
    private const string BaseUrl = "https://api.nexusmods.com/v1/";
    private const string GameDomain = "stardewvalley";

    private readonly HttpClient _httpClient;
    private bool _isPremium;

    public bool IsConfigured => _httpClient.DefaultRequestHeaders.Contains("apikey");
    public bool IsPremium => _isPremium;

    public NexusModsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("application-name", "SophisticatedModManager");
        _httpClient.DefaultRequestHeaders.Add("application-version", "1.0.0");
    }

    public void Configure(string apiKey)
    {
        if (_httpClient.DefaultRequestHeaders.Contains("apikey"))
            _httpClient.DefaultRequestHeaders.Remove("apikey");

        if (!string.IsNullOrWhiteSpace(apiKey))
            _httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
    }

    public async Task<NexusUserValidation?> ValidateApiKeyAsync(string apiKey)
    {
        var previousKey = IsConfigured;
        Configure(apiKey);

        try
        {
            var response = await _httpClient.GetAsync("users/validate.json");
            if (!response.IsSuccessStatusCode)
            {
                if (!previousKey) Configure("");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var validation = JsonSerializer.Deserialize<NexusUserValidation>(json);
            if (validation != null)
                _isPremium = validation.IsPremium;
            return validation;
        }
        catch
        {
            if (!previousKey) Configure("");
            return null;
        }
    }

    public async Task<NexusModInfo?> GetModInfoAsync(int modId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"games/{GameDomain}/mods/{modId}.json");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<NexusModInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<NexusModFile>> GetModFilesAsync(int modId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"games/{GameDomain}/mods/{modId}/files.json");
            if (!response.IsSuccessStatusCode) return new List<NexusModFile>();

            var json = await response.Content.ReadAsStringAsync();
            var fileList = JsonSerializer.Deserialize<NexusModFileList>(json);
            return fileList?.Files ?? new List<NexusModFile>();
        }
        catch
        {
            return new List<NexusModFile>();
        }
    }

    public async Task<string?> GetDownloadLinkAsync(int modId, int fileId, string? nxmKey = null, long? nxmExpires = null)
    {
        try
        {
            var url = $"games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json";

            if (nxmKey != null && nxmExpires != null)
                url += $"?key={nxmKey}&expires={nxmExpires}";

            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var links = JsonSerializer.Deserialize<List<NexusDownloadLink>>(json);
            return links?.FirstOrDefault()?.Uri;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> DownloadFileAsync(string downloadUrl, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        var uri = new Uri(downloadUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"nexus_download_{Guid.NewGuid():N}.zip";

        var filePath = Path.Combine(targetDir, fileName);

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await contentStream.CopyToAsync(fileStream);

        return filePath;
    }

    public async Task<List<NexusModInfo>> GetTrendingModsAsync()
    {
        return await GetModListAsync($"games/{GameDomain}/mods/trending.json");
    }

    public async Task<List<NexusModInfo>> GetLatestAddedModsAsync()
    {
        return await GetModListAsync($"games/{GameDomain}/mods/latest_added.json");
    }

    public async Task<List<NexusModInfo>> GetLatestUpdatedModsAsync()
    {
        return await GetModListAsync($"games/{GameDomain}/mods/latest_updated.json");
    }

    public async Task<List<int>> GetRecentlyUpdatedModIdsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"games/{GameDomain}/mods/updated.json?period=1m");
            if (!response.IsSuccessStatusCode) return new List<int>();

            var json = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<NexusUpdatedMod>>(json) ?? new List<NexusUpdatedMod>();
            return items.Select(i => i.ModId).ToList();
        }
        catch
        {
            return new List<int>();
        }
    }

    public async Task<Dictionary<string, List<string>>> GetChangelogAsync(int modId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"games/{GameDomain}/mods/{modId}/changelogs.json");
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, List<string>>();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                   ?? new Dictionary<string, List<string>>();
        }
        catch
        {
            return new Dictionary<string, List<string>>();
        }
    }

    public async Task<HashSet<int>> GetUserEndorsedModIdsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("user/endorsements.json");
            if (!response.IsSuccessStatusCode)
                return new HashSet<int>();

            var json = await response.Content.ReadAsStringAsync();
            var endorsements = JsonSerializer.Deserialize<List<NexusEndorsement>>(json) ?? new List<NexusEndorsement>();
            return endorsements
                .Where(e => e.DomainName.Equals(GameDomain, StringComparison.OrdinalIgnoreCase)
                            && e.Status.Equals("Endorsed", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.ModId)
                .ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    public async Task<bool> EndorseModAsync(int modId, string modVersion)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Version", modVersion) });
            var response = await _httpClient.PostAsync($"games/{GameDomain}/mods/{modId}/endorse.json", content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AbstainModAsync(int modId, string modVersion)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Version", modVersion) });
            var response = await _httpClient.PostAsync($"games/{GameDomain}/mods/{modId}/abstain.json", content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<NexusModInfo>> GetModListAsync(string endpoint)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode) return new List<NexusModInfo>();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<NexusModInfo>>(json) ?? new List<NexusModInfo>();
        }
        catch
        {
            return new List<NexusModInfo>();
        }
    }

    public int? ParseNexusModId(List<string> updateKeys)
    {
        foreach (var key in updateKeys)
        {
            if (key.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase))
            {
                var idStr = key[6..].Split('@')[0];
                if (int.TryParse(idStr, out var id))
                    return id;
            }
        }
        return null;
    }

    public NxmUrlInfo? ParseNxmUrl(string nxmUrl)
    {
        // Format: nxm://stardewvalley/mods/1234/files/5678?key=abc&expires=123
        if (!nxmUrl.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var uri = new Uri(nxmUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');

            // Expected: mods/{modId}/files/{fileId}
            if (segments.Length < 4 || segments[0] != "mods" || segments[2] != "files")
                return null;

            if (!int.TryParse(segments[1], out var modId) || !int.TryParse(segments[3], out var fileId))
                return null;

            var query = HttpUtility.ParseQueryString(uri.Query);

            return new NxmUrlInfo
            {
                GameDomain = uri.Host,
                ModId = modId,
                FileId = fileId,
                Key = query["key"],
                Expires = long.TryParse(query["expires"], out var exp) ? exp : null
            };
        }
        catch
        {
            return null;
        }
    }
}
