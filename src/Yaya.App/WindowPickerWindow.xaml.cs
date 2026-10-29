using System.Windows;
using System.Windows.Controls;
using Yaya.App.Services;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;

namespace Yaya.App;

/// <summary>
/// 用户主动打开的「选择窗口」窗口：一次性列出可见窗口，给进程/标题关键词建规则。
/// 红线：标题只在本次界面显示，不写日志、不落库；规则只保存用户确认的字符串。
/// </summary>
public partial class WindowPickerWindow : Window
{
    private readonly RuleManager _rules;
    private readonly Action _onSaved;

    public WindowPickerWindow(RuleManager rules, Action onSaved)
    {
        _rules = rules;
        _onSaved = onSaved;
        InitializeComponent();
        UpdateTargetUi();
        ReloadWindows();
    }

    private void ReloadWindows()
    {
        var windows = WindowEnumerator.Enumerate();
        WindowList.ItemsSource = windows;
        StatusText.Text = windows.Count == 0
            ? "没有找到可选择的窗口；切到目标窗口后点「刷新窗口」。"
            : $"共 {windows.Count} 个窗口；选中一个后设置规则，保存后立即生效。";
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowCandidate candidate)
        {
            return;
        }

        var result = new AppClassifier(_rules.Current).Classify(candidate.ProcessName, candidate.Title);
        CurrentHintText.Text = $"当前判定：{AppRulesViewModel.DescribeCategory(result)}";

        if (candidate.IsBrowser)
        {
            TitleRadio.IsChecked = true;
            RuleValueBox.Text = SuggestTitleKeyword(candidate.Title);
            BrowserOnlyBox.IsChecked = true;
            StatusText.Text = "浏览器请优先用标题关键词，避免把所有网页设成同一类。";
        }
        else
        {
            ProcessRadio.IsChecked = true;
            RuleValueBox.Text = candidate.ProcessName;
            BrowserOnlyBox.IsChecked = false;
            StatusText.Text = string.Empty;
        }

        SelectCategory(result.IsMuted
            ? RuleCategory.Ignore
            : result.Category == AppCategory.Unknown ? RuleCategory.Study : ToRuleCategory(result.Category));
        UpdateTargetUi();
    }

    private void OnTargetChanged(object sender, RoutedEventArgs e) => UpdateTargetUi();

    private void UpdateTargetUi()
    {
        // XAML 加载时 ProcessRadio 的 IsChecked 会先触发 Checked，此时 BrowserOnlyBox 还没实例化
        if (BrowserOnlyBox is null || TitleRadio is null)
        {
            return;
        }

        var isTitle = TitleRadio.IsChecked == true;
        BrowserOnlyBox.IsEnabled = isTitle;
        if (!isTitle)
        {
            BrowserOnlyBox.IsChecked = false;
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowCandidate candidate)
        {
            StatusText.Text = "先在上方选一个窗口。";
            return;
        }

        var value = RuleValueBox.Text.Trim();
        if (value.Length == 0 || value.Length > CategoryRule.MaxValueLength)
        {
            StatusText.Text = $"规则值需 1–{CategoryRule.MaxValueLength} 个字符。";
            return;
        }

        var target = TitleRadio.IsChecked == true ? RuleTarget.Title : RuleTarget.Process;
        var category = SelectedCategory;
        var rule = new CategoryRule(
            Guid.NewGuid().ToString("N"),
            target,
            target == RuleTarget.Process ? RuleMatch.Exact : RuleMatch.Contains,
            value,
            category,
            target == RuleTarget.Title && BrowserOnlyBox.IsChecked == true,
            Enabled: true);

        if (AppRules.Sanitize(new[] { rule }).Count == 0)
        {
            StatusText.Text = "规则值不合法，未保存。";
            return;
        }

        _rules.Upsert(rule);
        _onSaved();

        var kind = target == RuleTarget.Process ? "进程" : "标题";
        StatusText.Text = $"已保存：{candidate.ProcessName} → {CategoryLabel(category)}（{kind}规则）";
        if (target == RuleTarget.Process && candidate.IsBrowser)
        {
            StatusText.Text += "；注意：按进程会影响该浏览器的所有网页。";
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => ReloadWindows();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private RuleCategory SelectedCategory =>
        CategoryBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && Enum.TryParse<RuleCategory>(tag, out var category)
            ? category
            : RuleCategory.Study;

    private void SelectCategory(RuleCategory category)
    {
        foreach (var item in CategoryBox.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } && tag == category.ToString())
            {
                CategoryBox.SelectedItem = item;
                return;
            }
        }
    }

    // 浏览器标题优先建议站点词；没有站点词再用清洗后的标题（最多 64 字，仍可编辑）
    private static string SuggestTitleKeyword(string title)
    {
        var cleaned = BrowserTitleCleaner.Clean(title);
        if (SiteCatalog.TryMatch(cleaned, out var site))
        {
            return site.Keyword;
        }

        var value = cleaned.Length == 0 ? title.Trim() : cleaned;
        return value.Length > 64 ? value[..64] : value;
    }

    private static RuleCategory ToRuleCategory(AppCategory category) => category switch
    {
        AppCategory.Study => RuleCategory.Study,
        AppCategory.Utility => RuleCategory.Utility,
        AppCategory.Entertainment => RuleCategory.Entertainment,
        AppCategory.Social => RuleCategory.Social,
        _ => RuleCategory.Study,
    };

    private static string CategoryLabel(RuleCategory category) => category switch
    {
        RuleCategory.Study => "学习",
        RuleCategory.Utility => "工具",
        RuleCategory.Entertainment => "娱乐",
        RuleCategory.Social => "社交",
        _ => "不打扰",
    };
}
