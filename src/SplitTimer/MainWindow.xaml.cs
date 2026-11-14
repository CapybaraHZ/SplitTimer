using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SplitTimer.Models;
using SplitTimer.Services;

namespace SplitTimer;

public partial class MainWindow : Window
{
    private enum RoundState
    {
        Idle,
        Countdown,
        Running,
        Finished,
    }

    public static readonly DependencyProperty CardColumnsProperty = DependencyProperty.Register(
        nameof(CardColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(2));

    private readonly ObservableCollection<Player> _players = new();
    private readonly ObservableCollection<RankEntry> _rankEntries = new();
    private readonly Leaderboard _board = Leaderboard.Load();
    private readonly HashSet<Key> _heldKeys = new();
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _countdownClock = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private AppSettings _settings;
    private RoundState _state = RoundState.Idle;
    private Key _startKey = Key.Space;
    private Key _resetKey = Key.Escape;
    private bool _revealed;
    private bool _committed;
    private int _finishCount;
    private int _lastCountdownNumber = -1;
    private bool _windowActive = true;
    private bool _isFullScreen;
    private WindowStyle _savedStyle = WindowStyle.SingleBorderWindow;
    private WindowState _savedState = WindowState.Normal;

    public MainWindow()
    {
        _settings = SettingsStore.Load();

        InitializeComponent();
        ThemeHelper.ApplyDarkTitleBar(this);

        PlayerItems.ItemsSource = _players;
        RankItems.ItemsSource = _rankEntries;
        _uiTimer.Tick += (_, _) => OnTick();

        ApplyHotkeys();
        RebuildPlayers();
        RefreshAll();
    }

    public int CardColumns
    {
        get => (int)GetValue(CardColumnsProperty);
        set => SetValue(CardColumnsProperty, value);
    }

    // ============================================================
    // 快捷键（开始 / 重置）
    // ============================================================

    /// <summary>把设置里的键名解析成 Key，并把当前键位写到按钮与提示上。</summary>
    private void ApplyHotkeys()
    {
        _startKey = KeyNames.Parse(_settings.StartKey);
        if (_startKey == Key.None)
        {
            _startKey = Key.Space;
        }

        _resetKey = KeyNames.Parse(_settings.ResetKey);
        if (_resetKey == Key.None || _resetKey == _startKey)
        {
            _resetKey = _startKey == Key.Escape ? Key.Space : Key.Escape;
        }

        var startLabel = KeyNames.Friendly(_startKey);
        var resetLabel = KeyNames.Friendly(_resetKey);

        StartKeyText.Text = startLabel;
        ResetKeyText.Text = resetLabel;
        BtnStart.ToolTip = $"开始新一轮（{startLabel}）";
        BtnReset.ToolTip = $"重置整轮（{resetLabel}）";
    }

    // ============================================================
    // 玩家重建 / 列数
    // ============================================================

    private void RebuildPlayers()
    {
        _players.Clear();
        var defaults = AppSettings.CreateDefaultPlayers();

        for (var i = 0; i < _settings.PlayerCount; i++)
        {
            var setting = _settings.Players[i];

            // 配置文件被手动改坏（键名不合法）时退回默认键，别让玩家永远停不了表。
            var key = KeyNames.Parse(setting.Key);
            if (key == Key.None)
            {
                key = KeyNames.Parse(defaults[i].Key);
            }

            _players.Add(new Player
            {
                Index = i,
                Name = string.IsNullOrWhiteSpace(setting.Name) ? $"玩家 {i + 1}" : setting.Name,
                BindingKey = key,
                Status = PlayerStatus.Idle,
            });
        }

        CardColumns = ComputeColumns(_settings.PlayerCount);
        SubtitleText.Text = $"{_settings.PlayerCount} 位玩家 · 毫秒级精度";
    }

    private static int ComputeColumns(int count) => count switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 3,
        4 => 2,
        5 or 6 => 3,
        _ => 4,
    };

    // ============================================================
    // 一轮的生命周期
    // ============================================================

    private void StartRound()
    {
        if (_state is RoundState.Countdown or RoundState.Running)
        {
            return;
        }

        _revealed = false;
        _committed = false;
        _finishCount = 0;
        _clock.Reset();

        foreach (var player in _players)
        {
            player.Status = PlayerStatus.Idle;
            player.Result = null;
            player.FinishOrder = 0;
            player.Rank = 0;
        }

        if (_settings.CountdownEnabled)
        {
            _state = RoundState.Countdown;
            _lastCountdownNumber = -1;
            _countdownClock.Restart();
            CountdownText.Text = _settings.CountdownSeconds.ToString();
            CountdownOverlay.Visibility = Visibility.Visible;
            _uiTimer.Start();
        }
        else
        {
            BeginClock();
        }

        RefreshAll();
    }

    private void BeginClock()
    {
        CountdownOverlay.Visibility = Visibility.Collapsed;
        _countdownClock.Reset();
        _state = RoundState.Running;

        foreach (var player in _players)
        {
            if (player.Status == PlayerStatus.Idle)
            {
                player.Status = PlayerStatus.Running;
            }
        }

        // 计时起点只在这里产生：Stopwatch 走的是高精度计数器，UI 只是采样。
        _clock.Restart();
        _uiTimer.Start();
        RefreshAll();
    }

    private void StopPlayer(Player player)
    {
        if (player.Status != PlayerStatus.Running)
        {
            return;
        }

        // 成绩在按下的瞬间取，不受后续 UI 刷新影响。
        player.Result = _clock.Elapsed;
        player.Status = PlayerStatus.Stopped;
        player.FinishOrder = ++_finishCount;

        if (_players.All(p => p.Status == PlayerStatus.Stopped))
        {
            FinishRound();
        }
        else
        {
            RefreshAll();
        }
    }

    private void FinishRound()
    {
        _clock.Stop();
        _uiTimer.Stop();
        _state = RoundState.Finished;

        // 关闭自动隐藏时，本轮结束就等于结果已经公开。
        if (!_settings.AutoHideResults)
        {
            _revealed = true;
        }

        RefreshAll();
    }

    private void ResetRound()
    {
        _uiTimer.Stop();
        _clock.Reset();
        _countdownClock.Reset();
        CountdownOverlay.Visibility = Visibility.Collapsed;

        _state = RoundState.Idle;
        _revealed = false;
        _committed = false;
        _finishCount = 0;
        _lastCountdownNumber = -1;

        foreach (var player in _players)
        {
            player.Status = PlayerStatus.Idle;
            player.Result = null;
            player.FinishOrder = 0;
            player.Rank = 0;
        }

        RefreshAll();
    }

    // ============================================================
    // 刷新
    // ============================================================

    private void OnTick()
    {
        if (_state == RoundState.Countdown)
        {
            var remaining = _settings.CountdownSeconds - _countdownClock.Elapsed.TotalSeconds;
            var number = (int)Math.Ceiling(Math.Max(remaining, 0));
            if (number != _lastCountdownNumber)
            {
                _lastCountdownNumber = number;
                CountdownText.Text = Math.Max(number, 1).ToString();
            }

            if (remaining <= 0)
            {
                BeginClock();
            }

            return;
        }

        if (_state == RoundState.Running)
        {
            RefreshLiveReadout();
        }
    }

    /// <summary>每帧只动会变的数字，别去碰状态文案（避免无谓的布局与动画重启）。</summary>
    private void RefreshLiveReadout()
    {
        var text = TimeFormat.Format(_clock.Elapsed);
        foreach (var player in _players)
        {
            if (player.Status == PlayerStatus.Running)
            {
                player.TimeText = text;
            }
        }

        TotalTimeText.Text = text;
    }

    private void RefreshAll()
    {
        var reveal = _revealed || !_settings.AutoHideResults;

        // 只有真的把成绩公开了，才把本轮成绩计入榜单，避免提前泄露读数。
        if (reveal)
        {
            CommitResultsOnce();
        }

        var ranking = BuildRanking();
        var rankByPlayer = ranking.ToDictionary(entry => entry.PlayerIndex, entry => entry.Rank);

        foreach (var player in _players)
        {
            switch (player.Status)
            {
                case PlayerStatus.Idle:
                    player.TimeText = "00:00.000";
                    player.StatusText = _state switch
                    {
                        RoundState.Countdown => "等待发令",
                        RoundState.Finished => "未参与",
                        _ => "等待开始",
                    };
                    player.VisualState = "idle";
                    player.Rank = 0;
                    break;

                case PlayerStatus.Running:
                    player.TimeText = TimeFormat.Format(_clock.Elapsed);
                    player.StatusText = "计时中";
                    player.VisualState = "running";
                    player.Rank = 0;
                    break;

                case PlayerStatus.Stopped:
                    if (reveal)
                    {
                        player.TimeText = TimeFormat.Format(player.Result ?? TimeSpan.Zero);
                        player.StatusText = "已完成";
                        player.VisualState = "done";
                        player.Rank = rankByPlayer.TryGetValue(player.Index, out var rank) ? rank : 0;
                    }
                    else
                    {
                        player.TimeText = "\u2014\u2014:\u2014\u2014.\u2014\u2014\u2014";
                        player.StatusText = "已完成 · 读数已隐藏";
                        player.VisualState = "hidden";
                        player.Rank = 0;
                    }

                    break;
            }
        }

        TotalTimeText.Text = _state switch
        {
            RoundState.Running => TimeFormat.Format(_clock.Elapsed),
            RoundState.Finished => reveal ? TimeFormat.Format(_clock.Elapsed) : "\u2014\u2014:\u2014\u2014.\u2014\u2014\u2014",
            _ => "00:00.000",
        };

        UpdateRankBar(ranking);
        UpdateControls();
        UpdateHint();
        UpdateStatusPill();
        UpdateFullScreenButtons();
    }

    // ============================================================
    // 排名（按自定义时间周期统计）
    // ============================================================

    /// <summary>本轮成绩只入榜一次；窗口外的旧成绩顺手清掉，避免文件越滚越大。</summary>
    private void CommitResultsOnce()
    {
        if (_committed || _state != RoundState.Finished)
        {
            return;
        }

        var now = DateTime.Now;
        _board.Add(_players
            .Where(p => p.Status == PlayerStatus.Stopped && p.Result.HasValue)
            .Select(p => new ResultRecord
            {
                PlayerIndex = p.Index,
                PlayerName = p.Name,
                Seconds = p.Result!.Value.TotalSeconds,
                RecordedAt = now,
            }));

        _board.Prune(_settings.RankingWindowMinutes, now);
        _board.Save();
        _committed = true;
    }

    private List<RankEntry> BuildRanking()
    {
        if (_settings.RankingWindowMinutes <= 0)
        {
            return new List<RankEntry>();
        }

        return _board.Snapshot(
            _settings.RankingWindowMinutes,
            index => index >= 0 && index < _players.Count ? _players[index].Name : null,
            DateTime.Now);
    }

    private void UpdateRankBar(List<RankEntry> ranking)
    {
        var enabled = _settings.RankingWindowMinutes > 0;

        _rankEntries.Clear();
        foreach (var entry in ranking)
        {
            _rankEntries.Add(entry);
        }

        RankPeriodText.Text = enabled
            ? $"最近 {DescribeWindow(_settings.RankingWindowMinutes)} · 最好成绩"
            : "自动排名已关闭";
        RankPeriodText.Foreground = enabled
            ? (Brush)FindResource("GoldBrush")
            : (Brush)FindResource("TextMutedBrush");

        var hasEntries = ranking.Count > 0;
        RankItems.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;

        RankEmptyText.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
        RankEmptyText.Text = enabled
            ? "本周期内还没有成绩，跑完一轮就会出现在这里"
            : "在「设置」里填写统计周期（分钟）即可开启自动排名";

        BtnClearRank.IsEnabled = enabled && _board.Count > 0;
        BtnClearRank.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string DescribeWindow(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} 分钟";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} 小时" : $"{hours} 小时 {rest} 分";
    }

    private void BtnClearRank_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "清空后当前周期内的历史成绩都会被删除，之后的排名将从新一轮重新开始。继续吗？",
            "清空排名",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _board.Clear();
        _board.Save();

        // 已完成的这一轮如果还没入榜（比如还没点「显示结果」），保持原样；
        // 否则重新刷新会把清掉的成绩又写回去，所以这里只在空闲/重新开始时才允许重建。
        _committed = _state == RoundState.Finished && _revealed;
        RefreshAll();
    }

    private void UpdateControls()
    {
        BtnStart.IsEnabled = _state is RoundState.Idle or RoundState.Finished;
        BtnReset.IsEnabled = _state != RoundState.Idle;
        BtnSettings.IsEnabled = _state is not (RoundState.Running or RoundState.Countdown);

        var canReveal = _state == RoundState.Finished
                        && _settings.AutoHideResults
                        && !_revealed;
        BtnReveal.Visibility = canReveal ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHint()
    {
        if (!_windowActive)
        {
            HintText.Text = "窗口未激活，按键不会响应 · 点击窗口恢复";
            return;
        }

        var startLabel = KeyNames.Friendly(_startKey);
        var resetLabel = KeyNames.Friendly(_resetKey);

        HintText.Text = _state switch
        {
            RoundState.Idle => $"按 {startLabel} 开始新一轮 · 每位玩家按下自己的按键停表",
            RoundState.Countdown => $"准备 —— 倒计时结束后自动开始读秒 · 按 {resetLabel} 取消",
            RoundState.Running => $"计时中 —— 按下自己的按键锁定成绩 · 按 {resetLabel} 可重置本轮",
            RoundState.Finished => _revealed || !_settings.AutoHideResults
                ? $"本轮结束 · 按 {startLabel} 开始下一轮，按 {resetLabel} 回到就绪"
                : "全部完成 · 点「显示结果」查看成绩",
            _ => string.Empty,
        };
    }

    private void UpdateStatusPill()
    {
        var (text, accent) = _state switch
        {
            RoundState.Idle => ("就绪", "TextMutedBrush"),
            RoundState.Countdown => ("准备中", "AccentBrush"),
            RoundState.Running => ("计时中", "AccentBrush"),
            RoundState.Finished => _revealed || !_settings.AutoHideResults
                ? ("已揭示", "SuccessBrush")
                : ("待揭示", "GoldBrush"),
            _ => ("就绪", "TextMutedBrush"),
        };

        var brush = (Brush)FindResource(accent);
        StatusPillText.Text = text;
        StatusPillText.Foreground = brush;
        StatusDot.Fill = brush;
    }

    // ============================================================
    // 键盘：空格发令 + 各玩家自己的按键
    // ============================================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
        {
            key = e.ImeProcessedKey;
        }

        // 按住不放产生的自动重复一律忽略，一位玩家一次按键只能停一次表。
        if (e.IsRepeat || !_heldKeys.Add(key))
        {
            e.Handled = true;
            return;
        }

        if (key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        HandleKey(key);
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _heldKeys.Remove(key);
        if (e.Key == Key.ImeProcessed)
        {
            _heldKeys.Remove(e.ImeProcessedKey);
        }
    }

    private void HandleKey(Key key)
    {
        // 重置键在除「就绪」以外的任何状态都生效，和底部「重置」按钮保持一致。
        if (_state != RoundState.Idle && key == _resetKey)
        {
            ResetRound();
            return;
        }

        if (_state == RoundState.Running)
        {
            var player = _players.FirstOrDefault(p =>
                p.Status == PlayerStatus.Running && p.BindingKey == key);
            if (player is not null)
            {
                StopPlayer(player);
            }

            return;
        }

        if (key == _startKey)
        {
            StartRound();
        }
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            // 先退出最大化再恢复窗口样式，否则 WPF 会在改样式时抖动。
            WindowState = WindowState.Normal;
            WindowStyle = _savedStyle;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _savedState;
            _isFullScreen = false;
        }
        else
        {
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
        }

        UpdateFullScreenButtons();
    }

    private void UpdateFullScreenButtons()
    {
        BtnFullScreen.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
        BtnExitFullScreen.Visibility = _isFullScreen ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============================================================
    // 窗口 / 按钮事件
    // ============================================================

    private void Window_Activated(object sender, EventArgs e)
    {
        _windowActive = true;
        UpdateHint();
        Keyboard.Focus(this);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 失焦时清空按键记录，避免回到窗口后残留"按住"状态。
        _heldKeys.Clear();
        _windowActive = false;
        UpdateHint();
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e) => StartRound();

    private void BtnReset_Click(object sender, RoutedEventArgs e) => ResetRound();

    private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_isFullScreen)
        {
            ToggleFullScreen();
        }

        Keyboard.Focus(this);
    }

    private void BtnExitFullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen)
        {
            ToggleFullScreen();
        }

        Keyboard.Focus(this);
    }

    private void BtnReveal_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RoundState.Finished)
        {
            return;
        }

        _revealed = true;
        RefreshAll();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_state is RoundState.Running or RoundState.Countdown)
        {
            return;
        }

        var dialog = new SettingsWindow(_settings.Clone())
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _settings = dialog.Result;
            SettingsStore.Save(_settings);

            ApplyHotkeys();
            RebuildPlayers();
            ResetRound();
        }

        Keyboard.Focus(this);
    }
}
