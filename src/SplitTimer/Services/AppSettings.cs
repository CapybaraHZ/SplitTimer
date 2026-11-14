using SplitTimer.Models;

namespace SplitTimer.Services;

public sealed class PlayerSetting
{
    public string Name { get; set; } = string.Empty;

    /// <summary>保存为 WPF Key 枚举名字符串，例如 "A" / "NumPad1"。</summary>
    public string Key { get; set; } = "A";

    public PlayerSetting Clone() => new() { Name = Name, Key = Key };
}

public sealed class AppSettings
{
    /// <summary>参与本轮的玩家数量（1~8）。</summary>
    public int PlayerCount { get; set; } = 4;

    /// <summary>玩家按停后是否自动隐藏自己的读数。</summary>
    public bool AutoHideResults { get; set; } = true;

    /// <summary>开始前是否走 3-2-1 准备倒计时。</summary>
    public bool CountdownEnabled { get; set; } = true;

    public int CountdownSeconds { get; set; } = 3;

    /// <summary>
    /// 自动排名的统计周期（分钟）。只统计最近 N 分钟内的成绩，
    /// 填 0 表示关闭自动排名——不显示任何名次。
    /// </summary>
    public int RankingWindowMinutes { get; set; }

    /// <summary>发令开始新一轮的快捷键，保存为 WPF Key 枚举名（默认空格）。</summary>
    public string StartKey { get; set; } = DefaultStartKey;

    /// <summary>重置整轮的快捷键，保存为 WPF Key 枚举名（默认 Esc）。</summary>
    public string ResetKey { get; set; } = DefaultResetKey;

    public List<PlayerSetting> Players { get; set; } = CreateDefaultPlayers();

    public const int MinPlayers = 1;
    public const int MaxPlayers = 8;
    public const int MaxRankingWindowMinutes = 1440;
    public const string DefaultStartKey = "Space";
    public const string DefaultResetKey = "Escape";

    private static readonly string[] DefaultKeys =
    {
        "A", "L", "D1", "D0", "K", "J", "D3", "D8",
    };

    public static List<PlayerSetting> CreateDefaultPlayers()
    {
        var list = new List<PlayerSetting>();
        for (var i = 0; i < MaxPlayers; i++)
        {
            list.Add(new PlayerSetting
            {
                Name = $"玩家 {i + 1}",
                Key = DefaultKeys[i],
            });
        }

        return list;
    }

    public AppSettings Clone() => new()
    {
        PlayerCount = PlayerCount,
        AutoHideResults = AutoHideResults,
        CountdownEnabled = CountdownEnabled,
        CountdownSeconds = CountdownSeconds,
        RankingWindowMinutes = RankingWindowMinutes,
        StartKey = StartKey,
        ResetKey = ResetKey,
        Players = Players.Select(p => p.Clone()).ToList(),
    };

    /// <summary>把外部读入的对象修补成合法状态（数量、列表长度、空名字）。</summary>
    public void Normalize()
    {
        PlayerCount = Math.Clamp(PlayerCount, MinPlayers, MaxPlayers);
        CountdownSeconds = Math.Clamp(CountdownSeconds, 1, 10);
        RankingWindowMinutes = Math.Clamp(RankingWindowMinutes, 0, MaxRankingWindowMinutes);

        StartKey = NormalizeKey(StartKey, DefaultStartKey);
        ResetKey = NormalizeKey(ResetKey, DefaultResetKey);

        // 开始键与重置键撞在一起时保留开始键，重置键退回默认。
        if (SameKey(StartKey, ResetKey))
        {
            ResetKey = SameKey(StartKey, DefaultResetKey) ? DefaultStartKey : DefaultResetKey;
        }

        Players ??= CreateDefaultPlayers();

        while (Players.Count < MaxPlayers)
        {
            Players.Add(new PlayerSetting
            {
                Name = $"玩家 {Players.Count + 1}",
                Key = DefaultKeys[Players.Count],
            });
        }

        if (Players.Count > MaxPlayers)
        {
            Players.RemoveRange(MaxPlayers, Players.Count - MaxPlayers);
        }

        for (var i = 0; i < Players.Count; i++)
        {
            var item = Players[i];
            if (item is null)
            {
                Players[i] = item = new PlayerSetting { Name = $"玩家 {i + 1}", Key = DefaultKeys[i] };
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                item.Name = $"玩家 {i + 1}";
            }

            // 键名写坏时退回该玩家的默认键，别让人永远停不了表。
            var key = KeyNames.Parse(item.Key);
            item.Key = key == System.Windows.Input.Key.None ? DefaultKeys[i] : key.ToString();
        }

        // 停表键和「重置」撞车会让这位玩家按不出成绩（重置优先），换一个默认键。
        var used = new HashSet<string>(
            Players.Take(PlayerCount).Select(p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < PlayerCount; i++)
        {
            var item = Players[i];
            if (!SameKey(item.Key, ResetKey))
            {
                continue;
            }

            var replacement = DefaultKeys.FirstOrDefault(candidate =>
                !SameKey(candidate, ResetKey) && !used.Contains(candidate));

            if (replacement is null)
            {
                continue;
            }

            used.Remove(item.Key);
            used.Add(replacement);
            item.Key = replacement;
        }
    }

    private static string NormalizeKey(string? value, string fallback)
    {
        var key = KeyNames.Parse(value);
        return key == System.Windows.Input.Key.None ? fallback : key.ToString();
    }

    private static bool SameKey(string left, string right) =>
        KeyNames.Parse(left) == KeyNames.Parse(right);
}
