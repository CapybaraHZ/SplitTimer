using System.Windows.Input;

namespace SplitTimer.Models;

/// <summary>
/// 把 WPF 的 Key 枚举翻译成界面上好读的名字。
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<Key, string> Map = new()
    {
        [Key.Space] = "空格",
        [Key.Enter] = "回车",
        [Key.Tab] = "Tab",
        [Key.Back] = "退格",
        [Key.Escape] = "Esc",
        [Key.LeftShift] = "左 Shift",
        [Key.RightShift] = "右 Shift",
        [Key.LeftCtrl] = "左 Ctrl",
        [Key.RightCtrl] = "右 Ctrl",
        [Key.LeftAlt] = "左 Alt",
        [Key.RightAlt] = "右 Alt",
        [Key.Up] = "↑",
        [Key.Down] = "↓",
        [Key.Left] = "←",
        [Key.Right] = "→",
        [Key.OemComma] = ",",
        [Key.OemPeriod] = ".",
        [Key.OemMinus] = "-",
        [Key.OemPlus] = "=",
        [Key.Oem1] = ";",
        [Key.Oem2] = "/",
        [Key.Oem3] = "`",
        [Key.Oem4] = "[",
        [Key.Oem5] = "\\",
        [Key.Oem6] = "]",
        [Key.Oem7] = "'",
        [Key.Add] = "小键盘 +",
        [Key.Subtract] = "小键盘 -",
        [Key.Multiply] = "小键盘 *",
        [Key.Divide] = "小键盘 /",
        [Key.Decimal] = "小键盘 .",
        [Key.Capital] = "CapsLock",
        [Key.Apps] = "菜单键",
        [Key.Insert] = "Insert",
        [Key.Delete] = "Delete",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "PgUp",
        [Key.PageDown] = "PgDn",
    };

    public static string Friendly(Key key)
    {
        if (Map.TryGetValue(key, out var text))
        {
            return text;
        }

        if (key >= Key.A && key <= Key.Z)
        {
            return key.ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return "小键盘 " + ((int)(key - Key.NumPad0));
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return key.ToString();
        }

        return key.ToString();
    }

    /// <summary>从设置里保存的字符串还原按键，失败时退回 None。</summary>
    public static Key Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Key.None;
        }

        return Enum.TryParse<Key>(value, ignoreCase: true, out var key) ? key : Key.None;
    }
}
