namespace SophisticatedModManager.Services;

public interface IGameLauncherService
{
    void LaunchGame(string gamePath);
    bool IsGameRunning();
}
