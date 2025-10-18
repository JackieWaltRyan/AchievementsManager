using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;

namespace AchievementsManager;

internal sealed class AchievementsManager : IGitHubPluginUpdates, IBotModules {
    public string Name => nameof(AchievementsManager);
    public string RepositoryName => "JackieWaltRyan/AchievementsManager";
    public Version Version => typeof(AchievementsManager).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

    public Dictionary<string, AchievementsManagerConfig> AchievementsManagerConfig = new();
    public Dictionary<string, Dictionary<string, Timer>> AchievementsManagerTimers = new();

    public Task OnLoaded() => Task.CompletedTask;

    public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
        if (additionalConfigProperties != null) {
            if (AchievementsManagerTimers.TryGetValue(bot.BotName, out Dictionary<string, Timer>? dict)) {
                foreach (KeyValuePair<string, Timer> timers in dict) {
                    await timers.Value.DisposeAsync().ConfigureAwait(false);

                    bot.ArchiLogger.LogGenericInfo($"{timers.Key} Dispose.");
                }
            }

            AchievementsManagerTimers[bot.BotName] = new Dictionary<string, Timer> {
                { "GetAllAchievements", new Timer(async e => await GetAllAchievements(bot).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite) }
            };

            AchievementsManagerConfig[bot.BotName] = new AchievementsManagerConfig();

            foreach (KeyValuePair<string, JsonElement> configProperty in additionalConfigProperties) {
                switch (configProperty.Key) {
                    case "AchievementsManagerConfig": {
                        AchievementsManagerConfig? config = configProperty.Value.ToJsonObject<AchievementsManagerConfig>();

                        if (config != null) {
                            AchievementsManagerConfig[bot.BotName] = config;
                        }

                        break;
                    }
                }
            }

            if (AchievementsManagerConfig[bot.BotName].AddAchievements || AchievementsManagerConfig[bot.BotName].DelAchievements) {
                bot.ArchiLogger.LogGenericInfo($"AchievementsManagerConfig: {AchievementsManagerConfig[bot.BotName].ToJsonText()}");

                AchievementsManagerTimers[bot.BotName]["GetAllAchievements"].Change(1, -1);
            }
        }
    }

    public async Task GetAllAchievements(Bot bot) {
        if (bot.IsConnectedAndLoggedOn) {
            List<uint> addData = [];

            AchievementsManagerTimers[bot.BotName]["AddAchievements"] = new Timer(async e => await AddAchievements(bot, addData).ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);

            ObjectResponse<GetOwnedGamesResponse>? rawResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<GetOwnedGamesResponse>(new Uri($"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?access_token={bot.AccessToken}&steamid={bot.SteamID}&include_played_free_games=true&include_free_sub=true&skip_unvetted_apps=false")).ConfigureAwait(false);

            List<GetOwnedGamesResponse.ResponseData.Game>? games = rawResponse?.Content?.Response?.Games;

            if (games != null) {
                bot.ArchiLogger.LogGenericInfo($"Total games found: {games.Count}");

                if (games.Count > 0) {
                    if (AchievementsManagerConfig[bot.BotName].BlackList.Count > 0) {
                        bot.ArchiLogger.LogGenericInfo($"BlackList: {AchievementsManagerConfig[bot.BotName].BlackList.ToJsonText()}");
                    }

                    Dictionary<string, string> data = new() {
                        { "access_token", bot.AccessToken ?? string.Empty },
                        { "steamid", $"{bot.SteamID}" },
                        { "include_unvetted_apps", "true" }
                    };

                    uint index = 0;

                    foreach (GetOwnedGamesResponse.ResponseData.Game game in games) {
                        if (AchievementsManagerConfig[bot.BotName].BlackList.Contains(game.AppId)) {
                            continue;
                        }

                        data[$"appids[{index}]"] = $"{game.AppId}";

                        index += 1;
                    }

                    ObjectResponse<GetAchievementsProgressResponse>? rawApResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<GetAchievementsProgressResponse>(new Uri("https://api.steampowered.com/IPlayerService/GetAchievementsProgress/v1/"), data: data, session: ArchiWebHandler.ESession.None).ConfigureAwait(false);

                    List<GetAchievementsProgressResponse.ResponseData.Game>? response = rawApResponse?.Content?.Response?.AchievementProgress;

                    if ((response != null) && (response.Count > 0)) {
                        foreach (GetAchievementsProgressResponse.ResponseData.Game game in response) {
                            if (!game.AllUnlocked) {
                                addData.Add(game.AppId);
                            }
                        }

                        if (AchievementsManagerConfig[bot.BotName].AddAchievements) {
                            bot.ArchiLogger.LogGenericInfo($"Add achievements games found: {addData.Count}");

                            AchievementsManagerTimers[bot.BotName]["AddAchievements"].Change(1, -1);
                        }

                        return;
                    }

                    bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
                } else {
                    bot.ArchiLogger.LogGenericInfo($"Status: GameListIsEmpty | Next run: {DateTime.Now.AddHours(AchievementsManagerConfig[bot.BotName].Timeout):T}");

                    AchievementsManagerTimers[bot.BotName]["GetAllAchievements"].Change(TimeSpan.FromHours(AchievementsManagerConfig[bot.BotName].Timeout), TimeSpan.FromMilliseconds(-1));

                    return;
                }
            } else {
                bot.ArchiLogger.LogGenericInfo($"Status: Error | Next run: {DateTime.Now.AddMinutes(1):T}");
            }
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Next run: {DateTime.Now.AddMinutes(1):T}");
        }

        AchievementsManagerTimers[bot.BotName]["GetAllAchievements"].Change(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1));
    }

    public async Task AddAchievements(Bot bot, List<uint> addData) {
        uint timeout = 1;

        if (addData.Count > 0) {
            if (bot.IsConnectedAndLoggedOn) {
                uint gameId = addData[0];

                ObjectResponse<AddReviewResponse>? rawResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<AddReviewResponse>(
                    new Uri($"{ArchiWebHandler.SteamStoreURL}/friends/recommendgame?l=english"), data: new Dictionary<string, string>(9) {
                        { "appid", $"{gameId}" },
                        { "steamworksappid", $"{gameId}" },
                        { "comment", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.Comment },
                        { "rated_up", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.RatedUp.ToString() },
                        { "is_public", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.IsPublic.ToString() },
                        { "language", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.Language },
                        { "received_compensation", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.IsFree ? "1" : "0" },
                        { "disable_comments", AchievementsManagerConfig[bot.BotName].AddAchievementsConfig.AllowComments ? "0" : "1" }
                    }, referer: new Uri($"{ArchiWebHandler.SteamStoreURL}/app/{gameId}")
                ).ConfigureAwait(false);

                AddReviewResponse? response = rawResponse?.Content;

                if (response != null) {
                    if (response.Success) {
                        addData.RemoveAt(0);

                        bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: OK | Queue: {addData.Count}");

                        AchievementsManagerTimers[bot.BotName]["AddAchievements"].Change(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(-1));

                        return;
                    }

                    if ((response.StrError != null) && response.StrError.Contains("Please try again at a later time.", StringComparison.OrdinalIgnoreCase)) {
                        timeout = 10;

                        bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: RateLimitExceeded | Queue: {addData.Count} | Next run: {DateTime.Now.AddMinutes(timeout):T}");
                    } else {
                        addData.RemoveAt(0);

                        bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: {response.StrError} | Queue: {addData.Count} | Next run: {DateTime.Now.AddMinutes(timeout):T}");
                    }
                } else {
                    bot.ArchiLogger.LogGenericInfo($"ID: {gameId} | Status: Error | Queue: {addData.Count} | Next run: {DateTime.Now.AddMinutes(timeout):T}");
                }
            } else {
                bot.ArchiLogger.LogGenericInfo($"Status: BotNotConnected | Queue: {addData.Count} | Next run: {DateTime.Now.AddMinutes(timeout):T}");
            }

            AchievementsManagerTimers[bot.BotName]["AddAchievements"].Change(TimeSpan.FromMinutes(timeout), TimeSpan.FromMilliseconds(-1));
        } else {
            bot.ArchiLogger.LogGenericInfo($"Status: QueueIsEmpty | Next run: {DateTime.Now.AddHours(AchievementsManagerConfig[bot.BotName].Timeout):T}");

            AchievementsManagerTimers[bot.BotName]["GetAllAchievements"].Change(TimeSpan.FromHours(AchievementsManagerConfig[bot.BotName].Timeout), TimeSpan.FromMilliseconds(-1));
        }
    }
}
