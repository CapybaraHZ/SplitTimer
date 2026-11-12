namespace SplitTimer.Models;

/// <summary>
/// 排名条上的一条记录：某个统计周期内的名次、玩家、最好成绩与参与次数。
/// 只做展示，不做通知——每次刷新整体重建，避免逐条 PropertyChanged。
/// </summary>
public sealed class RankEntry
{
    public int PlayerIndex { get; init; }

    public int Rank { get; init; }

    public string RankText => Rank.ToString();

    public string Name { get; init; } = string.Empty;

    public string TimeText { get; init; } = string.Empty;

    /// <summary>该玩家在统计周期内被计入的次数。</summary>
    public int Runs { get; init; }

    public string RunsText => Runs > 1 ? $"{Runs} 次" : string.Empty;

    public bool HasRuns => Runs > 1;

    /// <summary>第一名用金色徽标突出。</summary>
    public bool IsLeader => Rank == 1;
}
