using Yaya.Core.Monitoring;

namespace Yaya.App.Services;

/// <summary>
/// 用户规则内存管理器（App 层）：加载/保存 rules.json，提供增删改与变更事件。
/// 规则只在本机生效；Changed 由 App 用于热替换分类器并刷新界面。
/// </summary>
public sealed class RuleManager
{
    private readonly string _dataDir;
    private readonly List<CategoryRule> _rules;

    public event EventHandler? Changed;

    public RuleManager(string dataDir)
    {
        _dataDir = dataDir;
        _rules = RulesStore.Load(dataDir).ToList();
    }

    public IReadOnlyList<CategoryRule> Current => _rules;

    /// <summary>整体替换（规则列表编辑/排序后调用）；顺序即优先级。</summary>
    public void Replace(IEnumerable<CategoryRule> rules)
    {
        _rules.Clear();
        _rules.AddRange(AppRules.Sanitize(rules));
        PersistAndNotify();
    }

    /// <summary>新增或更新一条规则；重复项更新类别并提到所属组顶部。</summary>
    public void Upsert(CategoryRule rule)
    {
        var normalized = AppRules.Sanitize(new[] { rule });
        if (normalized.Count == 0)
        {
            return;
        }

        var candidate = normalized[0];
        _rules.RemoveAll(r => r.Target == candidate.Target
            && r.Match == candidate.Match
            && string.Equals(r.Value, candidate.Value, StringComparison.OrdinalIgnoreCase));
        InsertAtGroupTop(candidate);
        PersistAndNotify();
    }

    /// <summary>桌宠/当前应用一键：进程名精确规则。</summary>
    public void AddProcessRule(string processName, RuleCategory category)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        Upsert(new CategoryRule(
            Guid.NewGuid().ToString("N"),
            RuleTarget.Process,
            RuleMatch.Exact,
            processName,
            category,
            BrowserOnly: false,
            Enabled: true));
    }

    public bool HasProcessRule(string processName) =>
        _rules.Any(r => r.Target == RuleTarget.Process
            && r.Match == RuleMatch.Exact
            && string.Equals(r.Value, processName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>恢复默认：清空全部用户规则（内置表不动）。</summary>
    public void ResetToDefault()
    {
        _rules.Clear();
        PersistAndNotify();
    }

    private void InsertAtGroupTop(CategoryRule rule)
    {
        if (rule.Target == RuleTarget.Process)
        {
            _rules.Insert(0, rule);
            return;
        }

        var firstTitle = _rules.FindIndex(r => r.Target == RuleTarget.Title);
        _rules.Insert(firstTitle < 0 ? _rules.Count : firstTitle, rule);
    }

    private void PersistAndNotify()
    {
        RulesStore.Save(_dataDir, _rules);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
