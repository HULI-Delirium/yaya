using System.IO;
using System.Text.Json;
using Serilog;
using Yaya.Core.Services;

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

    /// <summary>宠物窗是否显示；隐藏后进程与采集继续，托盘/桌宠菜单可恢复。</summary>
    public bool PetVisible { get; set; } = true;

    /// <summary>首次运行引导是否已完成（关闭欢迎窗即写 true，之后不再弹）。</summary>
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>每日轻问展示上限（1–10，应用时夹紧；只过滤展示，Core 冷却不变）。</summary>
    public int NudgeDailyLimit { get; set; } = 3;

    /// <summary>免打扰时段开关（默认开，23:00–07:00；时段内不弹轻问气泡）。</summary>
    public bool QuietHoursEnabled { get; set; } = true;

    /// <summary>免打扰开始时刻（HH:mm；支持跨零点，如 23:00–07:00）。</summary>
    public string QuietStart { get; set; } = "23:00";

    /// <summary>免打扰结束时刻（HH:mm）。</summary>
    public string QuietEnd { get; set; } = "07:00";

    /// <summary>轻问展示计数所属本地日期（yyyy-MM-dd；跨天归零）。</summary>
    public string NudgeCountDate { get; set; } = string.Empty;

    /// <summary>当日已展示的轻问次数（持久化，重启不重置）。</summary>
    public int NudgeCountToday { get; set; }

    /// <summary>番茄钟专注时长（分钟，1–120，应用时夹紧）。</summary>
    public int FocusMinutes { get; set; } = 25;

    /// <summary>番茄钟休息时长（分钟，1–120，应用时夹紧）。</summary>
    public int BreakMinutes { get; set; } = 5;

    /// <summary>LLM 话术设置（api_key 仅本地保存；导出时剔除，红线）。</summary>
    public LlmSettings Llm { get; set; } = new();

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
