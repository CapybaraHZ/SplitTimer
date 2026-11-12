using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SplitTimer.Services;

/// <summary>
/// 设置落在 %AppData%\SplitTimer\settings.json，读写失败都不让程序崩，
/// 只是退回默认值（比赛现场最怕弹错误框）。
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SplitTimer");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = new AppSettings();
                fresh.Normalize();
                return fresh;
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            fallback.Normalize();
            return fallback;
        }
    }

    public static bool Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            // 先写临时文件再替换，避免写到一半断电留下半个 json。
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

            return true;
        }
        catch
        {
            return false;
        }
    }
}
