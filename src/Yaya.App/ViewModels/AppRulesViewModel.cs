using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yaya.App.Services;
using Yaya.Core.Monitoring;

namespace Yaya.App.ViewModels;

public sealed record RuleTargetOption(RuleTarget Value, string Label);

public sealed record RuleMatchOption(RuleMatch Value, string Label);

public sealed record RuleCategoryOption(RuleCategory Value, string Label);

/// <summary>
/// 单条规则的编辑行（可改启用/类别/仅浏览器；上下移与删除走父 VM）。
/// 规则值只读展示，避免误改；改值请删除后重加。
/// </summary>
public partial class RuleRowViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Action<RuleRowViewModel> _onMoveUp;
    private readonly Action<RuleRowViewModel> _onMoveDown;
    private readonly Action<RuleRowViewModel> _onRemove;

    public RuleRowViewModel(
        CategoryRule rule,
        Action onChanged,
        Action<RuleRowViewModel> onMoveUp,
        Action<RuleRowViewModel> onMoveDown,
        Action<RuleRowViewModel> onRemove)
    {
        Rule = rule;
        _enabled = rule.Enabled;
        _category = rule.Category;
        _browserOnly = rule.BrowserOnly;
        _onChanged = onChanged;
        _onMoveUp = onMoveUp;
        _onMoveDown = onMoveDown;
        _onRemove = onRemove;
    }

    public CategoryRule Rule { get; private set; }

    public string TargetMatchLabel =>
        $"{(Rule.Target == RuleTarget.Process ? "进程" : "标题")}·{(Rule.Match == RuleMatch.Exact ? "精确" : "包含")}";

    public string Value => Rule.Value;

    public bool IsTitle => Rule.Target == RuleTarget.Title;

    public IReadOnlyList<RuleCategoryOption> CategoryOptions => AppRulesViewModel.CategoryOptionList;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private RuleCategory _category;

    [ObservableProperty]
    private bool _browserOnly;

    partial void OnEnabledChanged(bool value)
    {
        Rule = Rule with { Enabled = value };
        _onChanged();
    }

    partial void OnCategoryChanged(RuleCategory value)
    {
        Rule = Rule with { Category = value };
        _onChanged();
    }

    partial void OnBrowserOnlyChanged(bool value)
    {
        Rule = Rule with { BrowserOnly = value };
        _onChanged();
    }

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}

/// <summary>
/// 「应用与网页」设置块：当前前台卡片 + 用户规则列表（进程组在前、标题组在后）+ 添加表单。
/// 规则变更经 RuleManager 落盘并热更新分类器；本类不直接碰窗口句柄。
/// </summary>
public partial class AppRulesViewModel : ObservableObject
{
    public static IReadOnlyList<RuleTargetOption> TargetOptionList { get; } = new[]
    {
        new RuleTargetOption(RuleTarget.Process, "进程"),
        new RuleTargetOption(RuleTarget.Title, "标题"),
    };

    public static IReadOnlyList<RuleMatchOption> MatchOptionList { get; } = new[]
    {
        new RuleMatchOption(RuleMatch.Exact, "精确"),
        new RuleMatchOption(RuleMatch.Contains, "包含"),
    };

    public static IReadOnlyList<RuleCategoryOption> CategoryOptionList { get; } = new[]
    {
        new RuleCategoryOption(RuleCategory.Study, "学习"),
        new RuleCategoryOption(RuleCategory.Utility, "工具"),
        new RuleCategoryOption(RuleCategory.Entertainment, "娱乐"),
        new RuleCategoryOption(RuleCategory.Social, "社交"),
        new RuleCategoryOption(RuleCategory.Ignore, "不打扰"),
    };

    private readonly RuleManager _rules;
    private readonly Action _resetRequested;
    private readonly Action _openSettings;

    public AppRulesViewModel(RuleManager rules, Action resetRequested, Action openSettings)
    {
        _rules = rules;
        _resetRequested = resetRequested;
        _openSettings = openSettings;
        Reload();
    }

    public IReadOnlyList<RuleTargetOption> TargetOptions => TargetOptionList;

    public IReadOnlyList<RuleMatchOption> MatchOptions => MatchOptionList;

    public IReadOnlyList<RuleCategoryOption> CategoryOptions => CategoryOptionList;

    public ObservableCollection<RuleRowViewModel> ProcessRules { get; } = new();

    public ObservableCollection<RuleRowViewModel> TitleRules { get; } = new();

    [ObservableProperty]
    private string _currentAppText = "未识别到前台应用";

    [ObservableProperty]
    private string _currentCategoryText = "未知（未识别）";

    [ObservableProperty]
    private string _currentReasonText = "芽芽还在观察，切到目标窗口后这里会更新。";

    [ObservableProperty]
    private string _summaryText = "规则 0 条";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private RuleTarget _newTarget = RuleTarget.Process;

    [ObservableProperty]
    private RuleMatch _newMatch = RuleMatch.Exact;

    [ObservableProperty]
    private string _newValue = string.Empty;

    [ObservableProperty]
    private RuleCategory _newCategory = RuleCategory.Study;

    [ObservableProperty]
    private bool _newBrowserOnly = true;

    public bool IsNewTitle => NewTarget == RuleTarget.Title;

    /// <summary>当前前台是否可一键建设进程规则；浏览器不可（避免把所有网页设成同一类）。</summary>
    public bool CanSetCurrent { get; private set; }

    public bool CurrentIsBrowser { get; private set; }

    public string CurrentProcessName { get; private set; } = string.Empty;

    private string? CurrentMatchedSite { get; set; }

    partial void OnNewTargetChanged(RuleTarget value)
    {
        OnPropertyChanged(nameof(IsNewTitle));
        if (value == RuleTarget.Process)
        {
            NewBrowserOnly = false;
        }
    }

    /// <summary>由 App 在每次分类变化/启动时调用；进程名为空表示未识别。</summary>
    public void UpdateCurrent(ClassificationResult result, string processName, bool isBrowser)
    {
        CurrentProcessName = processName;
        CurrentIsBrowser = isBrowser;
        CurrentMatchedSite = result.MatchedSite;
        CanSetCurrent = !string.IsNullOrWhiteSpace(processName) && !isBrowser;
        CurrentAppText = string.IsNullOrWhiteSpace(processName) ? "未识别到前台应用" : processName;
        CurrentCategoryText = BuildCategoryText(result);
        CurrentReasonText = result.Reason;
    }

    /// <summary>规则增删/排序/外部添加后重建列表（行编辑不需要，行内已同步）。</summary>
    public void Reload()
    {
        ProcessRules.Clear();
        TitleRules.Clear();
        foreach (var rule in _rules.Current)
        {
            var row = new RuleRowViewModel(rule, OnRowChanged, MoveUp, MoveDown, Remove);
            if (rule.Target == RuleTarget.Process)
            {
                ProcessRules.Add(row);
            }
            else
            {
                TitleRules.Add(row);
            }
        }

        SummaryText = $"规则 {_rules.Current.Count} 条";
    }

    [RelayCommand]
    private void AddRule()
    {
        var value = NewValue.Trim();
        if (value.Length == 0)
        {
            StatusMessage = "规则值不能为空。";
            return;
        }

        var rule = new CategoryRule(
            Guid.NewGuid().ToString("N"),
            NewTarget,
            NewMatch,
            value,
            NewCategory,
            NewTarget == RuleTarget.Title && NewBrowserOnly,
            Enabled: true);

        if (AppRules.Sanitize(new[] { rule }).Count == 0)
        {
            StatusMessage = $"规则值需 1–{CategoryRule.MaxValueLength} 个字符。";
            return;
        }

        _rules.Upsert(rule);
        Reload();
        NewValue = string.Empty;
        StatusMessage = "已添加。";
    }

    [RelayCommand]
    private void SetCurrentCategory(RuleCategory category)
    {
        if (string.IsNullOrWhiteSpace(CurrentProcessName))
        {
            StatusMessage = "还没识别到前台应用。";
            return;
        }

        if (CurrentIsBrowser)
        {
            PrefillTitleRule(CurrentMatchedSite);
            StatusMessage = "浏览器请用标题规则（已预填站点词）。";
            return;
        }

        _rules.AddProcessRule(CurrentProcessName, category);
        Reload();
        StatusMessage = $"已设为{CategoryLabelText(category)}：{CurrentProcessName}";
    }

    /// <summary>浏览器场景：把添加表单切到标题规则，并预填站点词（没有就留空等用户填）。</summary>
    public void PrefillTitleRule(string? siteKeyword)
    {
        NewTarget = RuleTarget.Title;
        NewMatch = RuleMatch.Contains;
        NewValue = siteKeyword ?? string.Empty;
        NewCategory = RuleCategory.Study;
        NewBrowserOnly = true;
        StatusMessage = "标题规则默认只对浏览器生效；可勾选「仅浏览器」关闭。";
        _openSettings();
    }

    [RelayCommand]
    private void ResetRules() => _resetRequested();

    private void OnRowChanged()
    {
        // 行内只改了启用/类别/仅浏览器：写回管理器，不重建列表（避免编辑焦点丢失）
        _rules.Replace(BuildOrderedRules());
    }

    private void MoveUp(RuleRowViewModel row)
    {
        var collection = row.IsTitle ? TitleRules : ProcessRules;
        var index = collection.IndexOf(row);
        if (index <= 0)
        {
            return;
        }

        collection.Move(index, index - 1);
        _rules.Replace(BuildOrderedRules());
        Reload();
    }

    private void MoveDown(RuleRowViewModel row)
    {
        var collection = row.IsTitle ? TitleRules : ProcessRules;
        var index = collection.IndexOf(row);
        if (index < 0 || index >= collection.Count - 1)
        {
            return;
        }

        collection.Move(index, index + 1);
        _rules.Replace(BuildOrderedRules());
        Reload();
    }

    private void Remove(RuleRowViewModel row)
    {
        var collection = row.IsTitle ? TitleRules : ProcessRules;
        collection.Remove(row);
        _rules.Replace(BuildOrderedRules());
        Reload();
    }

    private List<CategoryRule> BuildOrderedRules() =>
        ProcessRules.Select(row => row.Rule)
            .Concat(TitleRules.Select(row => row.Rule))
            .ToList();

    private static string BuildCategoryText(ClassificationResult result)
    {
        if (result.IsMuted)
        {
            return "不打扰（不计入专注）";
        }

        var label = CategoryLabelText(result.Category);
        return result.Source switch
        {
            ClassificationSource.UserProcess or ClassificationSource.UserTitle => $"{label}（你的规则）",
            ClassificationSource.BuiltinProcess => $"{label}（内置）",
            ClassificationSource.BuiltinSite => $"{label}（站点：{result.MatchedSite}）",
            ClassificationSource.BrowserFallback => "未知（浏览器未命中站点）",
            _ => "未知（未识别）",
        };
    }

    private static string CategoryLabelText(RuleCategory category) => category switch
    {
        RuleCategory.Study => "学习",
        RuleCategory.Utility => "工具",
        RuleCategory.Entertainment => "娱乐",
        RuleCategory.Social => "社交",
        _ => "不打扰",
    };

    private static string CategoryLabelText(AppCategory category) => category switch
    {
        AppCategory.Study => "学习",
        AppCategory.Utility => "工具",
        AppCategory.Entertainment => "娱乐",
        AppCategory.Social => "社交",
        _ => "未知",
    };
}
