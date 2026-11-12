using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SplitTimer.Models;
using SplitTimer.Services;

namespace SplitTimer;

/// <summary>设置窗口里的一行玩家：名字 + 绑定的按键。</summary>
public sealed class SettingRow : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _keyText = string.Empty;
    private bool _isActive = true;
    private bool _isCapturing;

    public int Index { get; init; }

    public string IndexLabel => (Index + 1).ToString();

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public Key Key { get; set; }

    public string KeyText
    {
        get => _keyText;
        set => Set(ref _keyText, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set => Set(ref _isCapturing, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<SettingRow> _rows = new();
    private readonly AppSettings _working;
    private SettingRow? _captureRow;

    public SettingsWindow(AppSettings settings)
    {
        _working = settings.Clone();
        _working.Normalize();

        InitializeComponent();
        ThemeHelper.ApplyDarkTitleBar(this);

        RowList.ItemsSource = _rows;
        BuildRows();

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
        if (sender is not FrameworkElement { Tag: SettingRow row } || !row.IsActive)
        {
            return;
        }

        if (ReferenceEquals(_captureRow, row))
        {
            EndCapture();
            return;
        }

        EndCapture();
        _captureRow = row;
        row.IsCapturing = true;
        CaptureHint.Text = $"请按下要绑定给「{row.Name}」的按键 · Esc 取消";
    }

    private void EndCapture()
    {
        if (_captureRow is not null)
        {
            _captureRow.IsCapturing = false;
            _captureRow = null;
        }

        CaptureHint.Text = "点击右侧按键框可重新绑定";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_captureRow is null)
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

        _captureRow.Key = key;
        _captureRow.KeyText = KeyNames.Friendly(key);
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

        Result = _working;
        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
}
