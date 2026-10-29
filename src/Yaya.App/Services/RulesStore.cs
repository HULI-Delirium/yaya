using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Yaya.Core.Monitoring;

namespace Yaya.App.Services;

/// <summary>
/// 用户分类规则的本地存储（%LOCALAPPDATA%\Yaya\rules.json）。
/// 只存用户输入的字符串；损坏/未知版本不阻塞启动，坏文件改名留证。
/// </summary>
public static class RulesStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>读取规则；文件不存在/损坏/版本过新时返回空列表（不崩、不覆盖坏文件）。</summary>
    public static IReadOnlyList<CategoryRule> Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "rules.json");
        if (!File.Exists(path))
        {
            return Array.Empty<CategoryRule>();
        }

        try
        {
            var file = JsonSerializer.Deserialize<RulesFile>(File.ReadAllText(path), ReadOptions);
            if (file is null)
            {
                return Array.Empty<CategoryRule>();
            }

            if (file.Version > CurrentVersion)
            {
                Log.Warning("rules.json 版本 {Version} 高于当前支持的 {Current}，本次忽略用户规则", file.Version, CurrentVersion);
                return Array.Empty<CategoryRule>();
            }

            var rules = file.Rules?.Select(dto => dto.ToRule()) ?? Enumerable.Empty<CategoryRule>();
            var sanitized = AppRules.Sanitize(rules);
            if (sanitized.Count != (file.Rules?.Count ?? 0))
            {
                Log.Warning("rules.json 有 {Dropped} 条非法规则已丢弃（空值/超长/未定义枚举）",
                    (file.Rules?.Count ?? 0) - sanitized.Count);
            }

            return sanitized;
        }
        catch (Exception ex)
        {
            // 任何读取/反序列化异常都降级为空规则，绝不阻塞启动
            Log.Warning(ex, "rules.json 读取失败，按空规则启动");
            TryMoveCorruptFile(path);
            return Array.Empty<CategoryRule>();
        }
    }

    /// <summary>原子写：先写临时文件再替换；失败只记日志，不影响运行。</summary>
    public static void Save(string dataDir, IReadOnlyList<CategoryRule> rules)
    {
        var path = Path.Combine(dataDir, "rules.json");
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(dataDir);
            var file = new RulesFile
            {
                Version = CurrentVersion,
                Rules = rules.Select(RuleDto.From).ToList(),
            };
            File.WriteAllText(temp, JsonSerializer.Serialize(file, WriteOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "rules.json 写入失败（规则仍在本次运行内生效）");
            TryDelete(temp);
        }
    }

    private static void TryMoveCorruptFile(string path)
    {
        try
        {
            var corrupt = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"rules.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(path, corrupt, overwrite: true);
            Log.Warning("rules.json 已改名为 {Corrupt}", corrupt);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "rules.json 坏文件改名失败");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不影响主流程
        }
    }

    private sealed class RulesFile
    {
        public int Version { get; set; } = CurrentVersion;

        public List<RuleDto>? Rules { get; set; }
    }

    // DTO 用于兼容手写文件：BrowserOnly/Enabled 缺失时给安全默认值
    private sealed class RuleDto
    {
        public string? Id { get; set; }

        public RuleTarget Target { get; set; }

        public RuleMatch Match { get; set; }

        public string? Value { get; set; }

        public RuleCategory Category { get; set; }

        public bool? BrowserOnly { get; set; }

        public bool? Enabled { get; set; }

        public CategoryRule ToRule() => new(
            Id ?? string.Empty,
            Target,
            Match,
            Value ?? string.Empty,
            Category,
            BrowserOnly ?? Target == RuleTarget.Title,
            Enabled ?? true);

        public static RuleDto From(CategoryRule rule) => new()
        {
            Id = rule.Id,
            Target = rule.Target,
            Match = rule.Match,
            Value = rule.Value,
            Category = rule.Category,
            BrowserOnly = rule.BrowserOnly,
            Enabled = rule.Enabled,
        };
    }
}
