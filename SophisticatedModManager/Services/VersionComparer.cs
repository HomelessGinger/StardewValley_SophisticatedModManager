namespace SophisticatedModManager.Services;

public static class VersionComparer
{
    public static bool IsNewer(string remoteVersion, string localVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion) || string.IsNullOrWhiteSpace(localVersion))
            return false;

        remoteVersion = remoteVersion.TrimStart('v', 'V');
        localVersion = localVersion.TrimStart('v', 'V');

        // Split off prerelease tag (e.g. "1.2.3-beta.1")
        var remoteParts = remoteVersion.Split('-', 2);
        var localParts = localVersion.Split('-', 2);

        var remoteNumeric = remoteParts[0];
        var localNumeric = localParts[0];

        var remoteSegments = remoteNumeric.Split('.');
        var localSegments = localNumeric.Split('.');

        var maxLen = Math.Max(remoteSegments.Length, localSegments.Length);
        for (int i = 0; i < maxLen; i++)
        {
            var r = i < remoteSegments.Length && int.TryParse(remoteSegments[i], out var rv) ? rv : 0;
            var l = i < localSegments.Length && int.TryParse(localSegments[i], out var lv) ? lv : 0;

            if (r > l) return true;
            if (r < l) return false;
        }

        var remoteHasPrerelease = remoteParts.Length > 1;
        var localHasPrerelease = localParts.Length > 1;

        if (!remoteHasPrerelease && localHasPrerelease)
            return true; // "1.2.3" is newer than "1.2.3-beta"

        return false;
    }
}
