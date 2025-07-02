using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;

namespace AchievementsManager;

internal sealed class AchievementsManager : IGitHubPluginUpdates {
    public string Name => nameof(AchievementsManager);
    public string RepositoryName => "JackieWaltRyan/AchievementsManager";
    public Version Version => typeof(AchievementsManager).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

    public Task OnLoaded() {
        ASF.ArchiLogger.LogGenericInfo($"Hello {Name}!");

        return Task.CompletedTask;
    }
}
