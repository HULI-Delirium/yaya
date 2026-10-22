using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yaya.App.Pet;
using Yaya.Core.Services;

namespace Yaya.App.ViewModels;

public enum WeekNodeState
{
    /// <summary>未来日：灰态占位。</summary>
    Future = 0,

    /// <summary>已过但非专注日：几何花苞。</summary>
    Bud = 1,

    /// <summary>专注日：开花（复用 pet_bloom.png，零新美术依赖）。</summary>
    Bloom = 2,
}

/// <summary>进化树单节点。</summary>
public sealed record WeekDayNode(string Label, WeekNodeState State, ImageSource? BloomImage);

/// <summary>周报窗视图模型：7 节点进化树 + 三行数字 + 一句下周建议（纯本地规则版）。</summary>
public partial class WeekReportViewModel : ObservableObject
{
    private const int FocusedDayThresholdMinutes = 30;   // 与 WeeklyReportOptions 默认一致

    public IReadOnlyList<WeekDayNode> Days { get; }

    [ObservableProperty]
    private string _lineTotal;

    [ObservableProperty]
    private string _lineFocusedDays;

    [ObservableProperty]
    private string _linePomodoros;

    [ObservableProperty]
    private string _adviceLine;

    public WeekReportViewModel(
        WeeklyReportData data,
        IReadOnlyList<DailyReportData?> weekDays,
        DateOnly today,
        DateOnly weekStart,
        PetAssets assets)
    {
        var labels = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        var bloom = assets.Get(PetVisualState.Bloom);
        var nodes = new List<WeekDayNode>(7);
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            WeekNodeState state;
            if (day > today)
            {
                state = WeekNodeState.Future;
            }
            else if ((i < weekDays.Count ? weekDays[i]?.FocusedMinutes : null) >= FocusedDayThresholdMinutes)
            {
                state = WeekNodeState.Bloom;
            }
            else
            {
                state = WeekNodeState.Bud;
            }

            nodes.Add(new WeekDayNode(labels[i], state, state == WeekNodeState.Bloom ? bloom : null));
        }

        Days = nodes;
        LineTotal = $"本周有效专注 {data.TotalFocusedMinutes} 分钟";
        LineFocusedDays = $"专注日 {data.FocusedDays}/7";
        LinePomodoros = $"番茄完成 {data.PomodorosCompleted} 个";
        AdviceLine = data.AdviceStartTime is { } time
            ? $"下周建议：{time:HH:mm} 先做最难的 {data.AdviceMinutes} 分钟"
            : $"下周建议：开始时先做最难的 {data.AdviceMinutes} 分钟";
    }
}
