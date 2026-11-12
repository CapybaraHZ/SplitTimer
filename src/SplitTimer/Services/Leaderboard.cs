using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SplitTimer.Models;

namespace SplitTimer.Services;

/// <summary>一次停表成绩。成绩统一换算成秒存盘，避免 TimeSpan 的 JSON 表示随版本变化。</summary>
public sealed class ResultRecord
{
    public int PlayerIndex { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public double Seconds { get; set; }

    public DateTime RecordedAt { get; set; }
}

public sealed class LeaderboardFile
{
    public List<ResultRecord> Records { get; set; } = new();
}

/// <summary>
/// 按「时间周期」排名：只有最近 N 分钟内的成绩参与排名，取每位玩家在该窗口内的最好成绩。
/// 周期为 0 时视为关闭自动排名，不产生任何名次。
/// 数据落在 %AppData%\SplitTimer\leaderboard.json，关掉程序再打开仍能看到窗口内的成绩。
/// </summary>
public sealed class Leaderboard
{
    /// <summary>兜底上限：就算周期设得很大也不会让文件无限增长。</summary>
    private const int MaxRecords = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<ResultRecord> _records = new();

    public static string FilePath => Path.Combine(SettingsStore.Directory, "leaderboard.json");

    public int Count => _records.Count;

    public static Leaderboard Load()
    {
        var board = new Leaderboard();
        try
        {
            if (!File.Exists(FilePath))
            {
                return board;
            }

            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize<LeaderboardFile>(json, JsonOptions);
            if (file?.Records is not null)
            {
                board._records.AddRange(file.Records.Where(r => r.Seconds > 0));
            }
        }
        catch
        {
            // 榜单只用于展示，文件坏了就当空榜，绝不让主流程崩。
            board._records.Clear();
        }

        board.Trim();
        return board;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Directory);
            var json = JsonSerializer.Serialize(new LeaderboardFile { Records = _records }, JsonOptions);

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        catch
        {
            // 存盘失败不影响本轮使用。
        }
    }

    public void Add(IEnumerable<ResultRecord> records)
    {
        _records.AddRange(records.Where(r => r.Seconds > 0));
        Trim();
    }

    public void Clear() => _records.Clear();

    /// <summary>
    /// 生成当前周期内的排名。windowMinutes 为 0 时返回空列表（关闭自动排名）。
    /// </summary>
    public List<RankEntry> Snapshot(int windowMinutes, Func<int, string?> nameOf, DateTime now)
    {
        if (windowMinutes <= 0)
        {
            return new List<RankEntry>();
        }

        var from = now.AddMinutes(-windowMinutes);

        var entries = _records
            .Where(r => r.RecordedAt >= from)
            .GroupBy(r => r.PlayerIndex)
            .Select(g => new
            {
                Index = g.Key,
                Best = g.Min(r => r.Seconds),
                Runs = g.Count(),
                Name = g.OrderByDescending(r => r.RecordedAt).First().PlayerName,
            })
            .OrderBy(x => x.Best)
            .ToList();

        var result = new List<RankEntry>(entries.Count);
        var rank = 0;
        double? previous = null;

        for (var i = 0; i < entries.Count; i++)
        {
            var item = entries[i];

            // 成绩相同并列同名次，下一名跳过（1、1、3）。
            if (previous is null || Math.Abs(item.Best - previous.Value) > 0.0005)
            {
                rank = i + 1;
                previous = item.Best;
            }

            result.Add(new RankEntry
            {
                PlayerIndex = item.Index,
                Rank = rank,
                Name = nameOf(item.Index) is { Length: > 0 } name ? name : item.Name,
                TimeText = TimeFormat.Format(TimeSpan.FromSeconds(item.Best)),
                Runs = item.Runs,
            });
        }

        return result;
    }

    /// <summary>按周期清理过期成绩；周期关闭时只保留最近 24 小时，避免文件无限膨胀。</summary>
    public void Prune(int windowMinutes, DateTime now)
    {
        var keepFrom = windowMinutes > 0
            ? now.AddMinutes(-windowMinutes)
            : now.AddHours(-24);

        var removed = _records.RemoveAll(r => r.RecordedAt < keepFrom);
        if (removed > 0)
        {
            Trim();
        }
    }

    private void Trim()
    {
        if (_records.Count <= MaxRecords)
        {
            return;
        }

        _records.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
        _records.RemoveRange(0, _records.Count - MaxRecords);
    }
}
