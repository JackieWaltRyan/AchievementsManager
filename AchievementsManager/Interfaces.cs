using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AchievementsManager;

internal sealed record AchievementsManagerConfig {
    [JsonInclude]
    public bool AddAchievements { get; set; }

    [JsonInclude]
    public bool DelAchievements { get; set; }

    [JsonInclude]
    public List<uint> BlackList { get; set; } = [];

    [JsonInclude]
    public uint Timeout { get; set; } = 6;

    [JsonConstructor]
    public AchievementsManagerConfig() { }
}

internal sealed record GetOwnedGamesResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("games")]
        public List<Game>? Games { get; set; }

        internal sealed record Game {
            [JsonPropertyName("appid")]
            public uint AppId { get; set; }
        }
    }
}

internal sealed record GetAchievementsProgressResponse {
    [JsonPropertyName("response")]
    public ResponseData? Response { get; set; }

    internal sealed record ResponseData {
        [JsonPropertyName("achievement_progress")]
        public List<Game>? AchievementProgress { get; set; }

        internal sealed record Game {
            [JsonPropertyName("appid")]
            public uint AppId { get; set; }

            [JsonPropertyName("all_unlocked")]
            public bool AllUnlocked { get; set; }
        }
    }
}
