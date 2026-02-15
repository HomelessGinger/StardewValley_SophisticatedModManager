using System.Text.Json.Serialization;

namespace SophisticatedModManager.Models;

public class NexusModInfo
{
    [JsonPropertyName("mod_id")]
    public int ModId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("picture_url")]
    public string PictureUrl { get; set; } = string.Empty;

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }
}

public class NexusModFileList
{
    [JsonPropertyName("files")]
    public List<NexusModFile> Files { get; set; } = new();
}

public class NexusModFile
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("size_in_bytes")]
    public long? SizeInBytes { get; set; }

    [JsonPropertyName("size_kb")]
    public long SizeKb { get; set; }

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;
}

public class NexusDownloadLink
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("short_name")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("URI")]
    public string Uri { get; set; } = string.Empty;
}

public class NexusUpdatedMod
{
    [JsonPropertyName("mod_id")]
    public int ModId { get; set; }

    [JsonPropertyName("latest_file_update")]
    public long LatestFileUpdate { get; set; }

    [JsonPropertyName("latest_mod_activity")]
    public long LatestModActivity { get; set; }
}

public class NexusUserValidation
{
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; set; }

    [JsonPropertyName("is_supporter")]
    public bool IsSupporter { get; set; }
}

public class NexusEndorsement
{
    [JsonPropertyName("mod_id")]
    public int ModId { get; set; }

    [JsonPropertyName("domain_name")]
    public string DomainName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
