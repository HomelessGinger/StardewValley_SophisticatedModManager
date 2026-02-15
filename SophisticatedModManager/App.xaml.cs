using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SophisticatedModManager.Services;
using SophisticatedModManager.ViewModels;
using SophisticatedModManager.Views;

namespace SophisticatedModManager;

public partial class App : Application
{
    private IServiceProvider _serviceProvider = null!;
    private const string PipeName = "SophisticatedModManager_NXM";
    private const string MutexName = "SophisticatedModManager_SingleInstance";
    private System.Threading.Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var nxmUrl = e.Args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));

        _instanceMutex = new System.Threading.Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            if (nxmUrl != null)
                SendNxmToRunningInstance(nxmUrl);
            Shutdown();
            return;
        }

        var services = new ServiceCollection();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<IGameLauncherService, GameLauncherService>();
        services.AddSingleton<IModsDetectionService, ModsDetectionService>();
        services.AddSingleton<INexusModsService, NexusModsService>();
        services.AddSingleton<IModConfigService, ModConfigService>();
        services.AddSingleton<ISharedModService, SharedModService>();
        services.AddSingleton<ISaveBackupService, SaveBackupService>();
        services.AddSingleton<MainViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        RegisterNxmProtocol();

        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
        };
        mainWindow.Show();

        StartNxmPipeServer();

        if (nxmUrl != null)
        {
            var vm = _serviceProvider.GetRequiredService<MainViewModel>();
            _ = vm.HandleNxmUrl(nxmUrl);
        }
    }

    private static void RegisterNxmProtocol()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm");
            key.SetValue("", "URL:NXM Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",0");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch
        {
        }
    }

    private void StartNxmPipeServer()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server);
                    var url = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(url))
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            var vm = _serviceProvider.GetRequiredService<MainViewModel>();

                            var window = MainWindow;
                            if (window != null)
                            {
                                if (window.WindowState == WindowState.Minimized)
                                    window.WindowState = WindowState.Normal;
                                window.Activate();
                            }

                            await vm.HandleNxmUrl(url);
                        });
                    }
                }
                catch
                {
                    await Task.Delay(100);
                }
            }
        });
    }

    private static void SendNxmToRunningInstance(string nxmUrl)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);

            using var writer = new StreamWriter(client);
            writer.WriteLine(nxmUrl);
            writer.Flush();
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
