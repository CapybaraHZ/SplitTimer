using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SplitTimer.Models;
using SplitTimer.Services;

namespace SplitTimer;

/// <summary>可以「点一下再按键」重新绑定的目标：玩家的停表键、开始 / 重置快捷键。</summary>
public abstract class CaptureTarget : INotifyPropertyChanged
{
    private string _keyText = string.Empty;
    private bool _isCapturing;

    public Key Key { get; set; }

    public string KeyText
    {
        get => _keyText;
        set => Set(ref _keyText, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set => Set(ref _isCapturing, value);
    }

    /// <summary>提示文案里怎么称呼这次绑定。</summary>
    public abstract string DisplayLabel { get; }

    public virtual bool CanCapture => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(name);
    }

    protected void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>设置窗口里的一行玩家：名字 + 绑定的按键。</summary>
public sealed class SettingRow : CaptureTarget
{
    private string _name = string.Empty;
    private bool _isActive = true;

    public int Index { get; init; }

    public string IndexLabel => (Index + 1).ToString();

    public string Name
    {
        get => _name;
        set
        {
            Set(ref _name, value);
            Raise(nameof(DisplayLabel));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            Set(ref _isActive, value);
            Raise(nameof(CanCapture));
        }
    }

    public override string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? $"玩家 {Index + 1}" : Name;

    public override bool CanCapture => IsActive;
}

/// <summary>设置窗口里的一条快捷键（开始 / 重置）。</summary>
public sealed class HotkeyRow : CaptureTarget
{
    public string Title { get; init; } = string.Empty;

    public override string DisplayLabel => Title;
}

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<SettingRow> _rows = new();
    private readonly AppSettings _working;
    private readonly HotkeyRow _startRow;
    private readonly HotkeyRow _resetRow;
    private CaptureTarget? _captureTarget;

    public SettingsWindow(AppSettings settings)
    {
        _working = settings.Clone();
        _working.Normalize();

        InitializeComponent();
        ThemeHelper.ApplyDarkTitleBar(this);

        RowList.ItemsSource = _rows;
        BuildRows();

        _startRow = BuildHotkeyRow("开始", _working.StartKey);
        _resetRow = BuildHotkeyRow("重置", _working.ResetKey);
        HotkeyList.ItemsSource = new[] { _startRow, _resetRow };

        ChkAutoHide.IsChecked = _working.AutoHideResults;
        ChkCountdown.IsChecked = _working.CountdownEnabled;
        TxtRankWindow.Text = _working.RankingWindowMinutes.ToString();
        UpdateCountUi();

        // 打开就把焦点放在第一个开关上，空格即可切换（也方便无鼠标操作）。
        Loaded += (_, _) => ChkAutoHide.Focus();
    }

    /// <summary>只有点「保存」才会有值，取消时为 null。</summary>
    public AppSettings? Result { get; private set; }

    private void BuildRows()
    {
        _rows.Clear();

        for (var i = 0; i < AppSettings.MaxPlayers; i++)
        {
            var setting = _working.Players[i];
            var key = KeyNames.Parse(setting.Key);
            _rows.Add(new SettingRow
            {
                Index = i,
                Name = setting.Name,
                Key = key,
                KeyText = KeyNames.Friendly(key),
            });
        }
    }

    private static HotkeyRow BuildHotkeyRow(string title, string keyName)
    {
        var key = KeyNames.Parse(keyName);
        if (key == Key.None)
        {
            key = title == "开始" ? Key.Space : Key.Escape;
        }

        return new HotkeyRow
        {
            Title = title,
            Key = key,
            KeyText = KeyNames.Friendly(key),
        };
    }

    private void UpdateCountUi()
    {
        PlayerCountText.Text = _working.PlayerCount.ToString();
        BtnMinus.IsEnabled = _working.PlayerCount > AppSettings.MinPlayers;
        BtnPlus.IsEnabled = _working.PlayerCount < AppSettings.MaxPlayers;

        foreach (var row in _rows)
        {
            row.IsActive = row.Index < _working.PlayerCount;
        }
    }

    private void BtnMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_working.PlayerCount <= AppSettings.MinPlayers)
        {
            return;
        }

        _working.PlayerCount--;
        UpdateCountUi();
    }

    private void BtnPlus_Click(object sender, RoutedEventArgs e)
    {
        if (_working.PlayerCount >= AppSettings.MaxPlayers)
        {
            return;
        }

        _working.PlayerCount++;
        UpdateCountUi();
    }

    // ============================================================
    // 按键捕获
    // ============================================================

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CaptureTarget target } || !target.CanCapture)
        {
            return;
        }

        // 再点一次同一个框就是取消，鼠标用户不必记 Esc。
        if (ReferenceEquals(_captureTarget, target))
        {
            EndCapture();
            return;
        }

        EndCapture();
        _captureTarget = target;
        target.IsCapturing = true;
        CaptureHint.Text = $"请按下要绑定给「{target.DisplayLabel}」的按键 · Esc 取消";
    }

    private void EndCapture()
    {
        if (_captureTarget is not null)
        {
            _captureTarget.IsCapturing = false;
            _captureTarget = null;
        }

        CaptureHint.Text = DefaultHint;
    }

    private const string DefaultHint = "点击按键框可重新绑定 · Esc 用于取消";

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_captureTarget is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
        {
            key = e.ImeProcessedKey;
        }

        if (key == Key.Escape)
        {
            EndCapture();
            e.Handled = true;
            return;
        }

        if (key == Key.None)
        {
            return;
        }

        _captureTarget.Key = key;
        _captureTarget.KeyText = KeyNames.Friendly(key);
        EndCapture();
        e.Handled = true;
    }

    // ============================================================
    // 底部按钮
    // ============================================================

    private void BtnDefault_Click(object sender, RoutedEventArgs e)
    {
        EndCapture();

        var defaults = AppSettings.CreateDefaultPlayers();
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Name = defaults[i].Name;
            _rows[i].Key = KeyNames.Parse(defaults[i].Key);
            _rows[i].KeyText = KeyNames.Friendly(_rows[i].Key);
        }

        _startRow.Key = KeyNames.Parse(AppSettings.DefaultStartKey);
        _startRow.KeyText = KeyNames.Friendly(_startRow.Key);
        _resetRow.Key = KeyNames.Parse(AppSettings.DefaultResetKey);
        _resetRow.KeyText = KeyNames.Friendly(_resetRow.Key);

        _working.PlayerCount = 4;
        ChkAutoHide.IsChecked = true;
        ChkCountdown.IsChecked = true;
        TxtRankWindow.Text = "0";
        UpdateCountUi();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        EndCapture();

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var name = string.IsNullOrWhiteSpace(row.Name) ? $"玩家 {i + 1}" : row.Name.Trim();
            row.Name = name;

            _working.Players[i].Name = name;
            _working.Players[i].Key = row.Key.ToString();
        }

        _working.AutoHideResults = ChkAutoHide.IsChecked == true;
        _working.CountdownEnabled = ChkCountdown.IsChecked == true;

        if (!int.TryParse(TxtRankWindow.Text.Trim(), out var windowMinutes)
            || windowMinutes < 0
            || windowMinutes > AppSettings.MaxRankingWindowMinutes)
        {
            Warn($"自动排名统计周期请填 0~{AppSettings.MaxRankingWindowMinutes} 之间的整数分钟，填 0 表示关闭自动排名。");
            TxtRankWindow.Focus();
            TxtRankWindow.SelectAll();
            return;
        }

        _working.RankingWindowMinutes = windowMinutes;

        var active = _working.Players.Take(_working.PlayerCount).ToList();
        if (active.Count == 0)
        {
            Warn("至少需要一位玩家参与。");
            return;
        }

        var empty = active.FirstOrDefault(p => KeyNames.Parse(p.Key) == Key.None);
        if (empty is not null)
        {
            Warn($"「{empty.Name}」还没有绑定按键。");
            return;
        }

        var duplicate = active
            .GroupBy(p => p.Key)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            var names = string.Join("、", active.Where(p => p.Key == duplicate.Key).Select(p => p.Name));
            Warn($"按键「{KeyNames.Friendly(KeyNames.Parse(duplicate.Key))}」被 {names} 同时占用，请改成不同的按键。");
            return;
        }

        if (_startRow.Key == Key.None || _resetRow.Key == Key.None)
        {
            Warn("「开始」和「重置」都需要绑定一个按键。");
            return;
        }

        if (_startRow.Key == _resetRow.Key)
        {
            Warn($"「开始」和「重置」不能都用「{KeyNames.Friendly(_startRow.Key)}」，请改成不同的按键。");
            return;
        }

        var startClash = active.FirstOrDefault(p => KeyNames.Parse(p.Key) == _startRow.Key);
        if (startClash is not null)
        {
            Warn($"按键「{KeyNames.Friendly(_startRow.Key)}」已经是「{startClash.Name}」的停表键，请给「开始」换一个键。");
            return;
        }

        var resetClash = active.FirstOrDefault(p => KeyNames.Parse(p.Key) == _resetRow.Key);
        if (resetClash is not null)
        {
            Warn($"按键「{KeyNames.Friendly(_resetRow.Key)}」已经是「{resetClash.Name}」的停表键，请给「重置」换一个键。");
            return;
        }

        _working.StartKey = _startRow.Key.ToString();
        _working.ResetKey = _resetRow.Key.ToString();

        Result = _working;
        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
}
