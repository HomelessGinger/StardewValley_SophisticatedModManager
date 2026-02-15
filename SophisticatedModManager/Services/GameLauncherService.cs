using System.Diagnostics;
using System.IO;

namespace SophisticatedModManager.Services;

public class GameLauncherService : IGameLauncherService
{
    public void LaunchGame(string gamePath)
    {
        var smapiPath = Path.Combine(gamePath, "StardewModdingAPI.exe");
        if (!File.Exists(smapiPath))
            throw new FileNotFoundException("SMAPI not found. Please check your game path in Settings.", smapiPath);

        // If this is a Steam copy, ensure Steam is running before launching SMAPI
        var steamAppIdPath = Path.Combine(gamePath, "steam_appid.txt");
        if (File.Exists(steamAppIdPath) && Process.GetProcessesByName("steam").Length == 0)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://",
                UseShellExecute = true
            });
            Thread.Sleep(5000);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = smapiPath,
            WorkingDirectory = gamePath,
            UseShellExecute = true
        });
    }

    public bool IsGameRunning()
    {
        return Process.GetProcessesByName("StardewModdingAPI").Length > 0
               || Process.GetProcessesByName("Stardew Valley").Length > 0;
    }
}
