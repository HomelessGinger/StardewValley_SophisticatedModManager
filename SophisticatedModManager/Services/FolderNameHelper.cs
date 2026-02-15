namespace SophisticatedModManager.Services;

/// <summary>
/// Helper methods for working with dot-prefixed folder names (enabled/disabled mods).
/// </summary>
public static class FolderNameHelper
{
    /// <summary>
    /// Removes the dot prefix from a folder name if present.
    /// </summary>
    public static string GetCleanName(string folderName)
    {
        return folderName.TrimStart('.');
    }

    /// <summary>
    /// Adds a dot prefix to a folder name if not already present.
    /// </summary>
    public static string AddDotPrefix(string folderName)
    {
        return folderName.StartsWith(".") ? folderName : "." + folderName;
    }

    /// <summary>
    /// Checks if a folder name has a dot prefix (disabled state).
    /// </summary>
    public static bool IsDisabled(string folderName)
    {
        return folderName.StartsWith(".");
    }

    /// <summary>
    /// Checks if a folder name does NOT have a dot prefix (enabled state).
    /// </summary>
    public static bool IsEnabled(string folderName)
    {
        return !folderName.StartsWith(".");
    }

    /// <summary>
    /// Creates the disabled version of a folder name (adds dot prefix).
    /// </summary>
    public static string ToDisabled(string folderName)
    {
        return AddDotPrefix(GetCleanName(folderName));
    }

    /// <summary>
    /// Creates the enabled version of a folder name (removes dot prefix).
    /// </summary>
    public static string ToEnabled(string folderName)
    {
        return GetCleanName(folderName);
    }

    /// <summary>
    /// Validates a name for use as a folder name (profile, collection, etc.).
    /// </summary>
    /// <param name="input">The name to validate</param>
    /// <param name="fieldLabel">Label for error messages (e.g., "Profile name", "Collection name")</param>
    /// <returns>Error message if validation fails, null if valid</returns>
    public static string? ValidateName(string input, string fieldLabel)
    {
        var name = input.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return $"{fieldLabel} cannot be empty.";

        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
            return $"{fieldLabel} contains invalid characters.";

        return null; // Valid
    }
}
