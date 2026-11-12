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

    public List<PlayerSetting> Players { get; set; } = CreateDefaultPlayers();

    public const int MinPlayers = 1;
    public const int MaxPlayers = 8;
    public const int MaxRankingWindowMinutes = 1440;

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
        Players = Players.Select(p => p.Clone()).ToList(),
    };

    /// <summary>把外部读入的对象修补成合法状态（数量、列表长度、空名字）。</summary>
    public void Normalize()
    {
        PlayerCount = Math.Clamp(PlayerCount, MinPlayers, MaxPlayers);
        CountdownSeconds = Math.Clamp(CountdownSeconds, 1, 10);
        RankingWindowMinutes = Math.Clamp(RankingWindowMinutes, 0, MaxRankingWindowMinutes);

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

            if (string.IsNullOrWhiteSpace(item.Key))
            {
                item.Key = DefaultKeys[i];
            }
        }
    }
}
