using System.IO;
using System.Text.Json;
using Serilog;

namespace Yaya.App.Services;

/// <summary>
/// 应用设置（本地优先：JSON 存于数据目录 settings.json，不含任何敏感信息）。
/// 任何读写失败都降级为默认值并记日志，绝不因设置损坏影响启动。
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>采集总开关；关闭时状态机暂停，恢复后从当前信号继续判定。</summary>
    public bool MonitoringEnabled { get; set; } = true;

    /// <summary>提醒展示开关：仅过滤 UI 展示，Core 照常判定与冷却（Q6 语义）。</summary>
    public bool NudgeEnabled { get; set; } = true;

    /// <summary>宠物窗鼠标穿透。</summary>
    public bool ClickThrough { get; set; }

    public static AppSettings Load(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, "settings.json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
                    ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "设置文件读取失败，使用默认设置");
        }

        return new AppSettings();
    }

    public void Save(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "settings.json"),
                JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "设置文件写入失败");
        }
    }
}
