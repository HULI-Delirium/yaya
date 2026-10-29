using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>
/// 历史窗单行：只含聚合数字与一句本地复盘文本，**不含窗口标题/进程名**（隐私红线）。
/// BarWidth 由 App 按 14 天最大值归一到 0–140px，不引入图表库。
/// </summary>
public sealed record HistoryDayRow(
    string DateLabel,
    int BarWidth,
    string MinutesLabel,
    string CorrectionLabel,
    string Note,
    bool IsEmpty);

/// <summary>
/// 「专注记录」窗视图模型（近 14 天，只读）：数据由 App 从 daily_summary 缓存/现算后传入，
/// 本类不碰数据库，也不改变任何状态。空态提供「开始专注」一键入口。
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly Action _onStartFocus;

    public IReadOnlyList<HistoryDayRow> Days { get; }

    public string WeekTotalText { get; }

    public string AverageText { get; }

    public string StreakText { get; }

    /// <summary>14 天全无记录时显示空态（列表隐藏）。</summary>
    public bool IsEmpty { get; }

    public HistoryViewModel(
        IReadOnlyList<HistoryDayRow> days,
        string weekTotalText,
        string averageText,
        string streakText,
        bool isEmpty,
        Action onStartFocus)
    {
        Days = days;
        WeekTotalText = weekTotalText;
        AverageText = averageText;
        StreakText = streakText;
        IsEmpty = isEmpty;
        _onStartFocus = onStartFocus;
    }

    [RelayCommand]
    private void StartFocus() => _onStartFocus();
}
