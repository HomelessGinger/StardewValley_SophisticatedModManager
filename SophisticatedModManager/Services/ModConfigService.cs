using System.IO;
using System.Text.Json;
using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public class ModConfigService : IModConfigService
{
    public bool HasConfig(string modFolderPath)
    {
        return File.Exists(Path.Combine(modFolderPath, "config.json"));
    }

    public List<ConfigEntry> LoadConfig(string modFolderPath)
    {
        var configPath = Path.Combine(modFolderPath, "config.json");
        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        return ParseElement(doc.RootElement, "", 0);
    }

    public void SaveConfig(string modFolderPath, List<ConfigEntry> entries)
    {
        var configPath = Path.Combine(modFolderPath, "config.json");
        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        WriteElement(writer, doc.RootElement, entries);

        writer.Flush();
        stream.Position = 0;
        var newJson = new StreamReader(stream).ReadToEnd();
        File.WriteAllText(configPath, newJson);
    }

    private static List<ConfigEntry> ParseElement(JsonElement element, string parentPath, int nestingLevel)
    {
        var entries = new List<ConfigEntry>();

        if (element.ValueKind != JsonValueKind.Object)
            return entries;

        foreach (var property in element.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(parentPath) ? property.Name : $"{parentPath}.{property.Name}";
            var entry = new ConfigEntry
            {
                Key = property.Name,
                JsonPath = path,
                NestingLevel = nestingLevel
            };

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    entry.ValueType = ConfigValueType.Boolean;
                    entry.BoolValue = property.Value.GetBoolean();
                    break;
                case JsonValueKind.Number:
                    entry.ValueType = ConfigValueType.Number;
                    entry.StringValue = property.Value.GetRawText();
                    break;
                case JsonValueKind.String:
                    entry.ValueType = ConfigValueType.String;
                    entry.StringValue = property.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Object:
                    entry.ValueType = ConfigValueType.Object;
                    entry.Children = ParseElement(property.Value, path, nestingLevel + 1);
                    break;
                case JsonValueKind.Array:
                    entry.ValueType = ConfigValueType.Array;
                    entry.StringValue = property.Value.GetRawText();
                    break;
                case JsonValueKind.Null:
                    entry.ValueType = ConfigValueType.Null;
                    entry.StringValue = "null";
                    break;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement original, List<ConfigEntry> entries)
    {
        var lookup = new Dictionary<string, ConfigEntry>();
        foreach (var e in entries)
            lookup[e.Key] = e;

        writer.WriteStartObject();

        foreach (var property in original.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);

            if (lookup.TryGetValue(property.Name, out var entry))
                WriteValue(writer, entry, property.Value);
            else
                property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, ConfigEntry entry, JsonElement original)
    {
        switch (entry.ValueType)
        {
            case ConfigValueType.Boolean:
                writer.WriteBooleanValue(entry.BoolValue);
                break;
            case ConfigValueType.Number:
                if (long.TryParse(entry.StringValue, out var longVal))
                    writer.WriteNumberValue(longVal);
                else if (double.TryParse(entry.StringValue, out var dblVal))
                    writer.WriteNumberValue(dblVal);
                else
                    original.WriteTo(writer);
                break;
            case ConfigValueType.String:
                writer.WriteStringValue(entry.StringValue);
                break;
            case ConfigValueType.Object:
                WriteElement(writer, original, entry.Children);
                break;
            case ConfigValueType.Array:
                try
                {
                    using var arrDoc = JsonDocument.Parse(entry.StringValue);
                    arrDoc.RootElement.WriteTo(writer);
                }
                catch
                {
                    original.WriteTo(writer);
                }
                break;
            case ConfigValueType.Null:
                writer.WriteNullValue();
                break;
        }
    }
}
