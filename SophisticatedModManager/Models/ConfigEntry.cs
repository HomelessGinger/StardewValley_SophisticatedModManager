namespace SophisticatedModManager.Models;

public enum ConfigValueType
{
    String,
    Number,
    Boolean,
    Object,
    Array,
    Null
}

public class ConfigEntry
{
    public string Key { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public ConfigValueType ValueType { get; set; }
    public string StringValue { get; set; } = string.Empty;
    public bool BoolValue { get; set; }
    public List<ConfigEntry> Children { get; set; } = new();
    public int NestingLevel { get; set; }
}
