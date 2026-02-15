using SophisticatedModManager.Models;

namespace SophisticatedModManager.Services;

public interface IConfigService
{
    bool Exists();
    AppConfig Load();
    void Save(AppConfig config);
}
