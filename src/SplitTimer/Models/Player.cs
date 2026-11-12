using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SplitTimer.Models;

public enum PlayerStatus
{
    /// <summary>本轮还没开始，或已被重置。</summary>
    Idle,

    /// <summary>计时中，读数实时滚动。</summary>
    Running,

    /// <summary>已按停，成绩已锁定。</summary>
    Stopped,
}

/// <summary>
/// 一位玩家。只负责持数据 + 通知，显示文案由 MainWindow 统一刷新，
/// 保证「隐藏 / 揭示」这类全局规则只有一个判定入口。
/// </summary>
public sealed class Player : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _timeText = "00:00.000";
    private string _statusText = "等待开始";
    private string _visualState = "idle";
    private int _rank;

    public int Index { get; init; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public Key BindingKey { get; set; }

    public string KeyText => KeyNames.Friendly(BindingKey);

    public PlayerStatus Status { get; set; } = PlayerStatus.Idle;

    /// <summary>精确成绩，只在按停的那一刻写入。</summary>
    public TimeSpan? Result { get; set; }

    /// <summary>完成顺序，1 表示本轮最先按停。</summary>
    public int FinishOrder { get; set; }

    /// <summary>大号读数文本。</summary>
    public string TimeText
    {
        get => _timeText;
        set => Set(ref _timeText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>idle / running / done / hidden，供 XAML 的 DataTrigger 切换配色。</summary>
    public string VisualState
    {
        get => _visualState;
        set => Set(ref _visualState, value);
    }

    public int Rank
    {
        get => _rank;
        set
        {
            if (Set(ref _rank, value))
            {
                OnPropertyChanged(nameof(RankText));
                OnPropertyChanged(nameof(HasRank));
            }
        }
    }

    public bool HasRank => _rank > 0;

    public string RankText => _rank > 0 ? $"第 {_rank} 名" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyKeyChanged() => OnPropertyChanged(nameof(KeyText));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
