using Yaya.Core.Monitoring;
using Yaya.Core.Storage;

namespace Yaya.Core.Services;

/// <summary>
/// 每日复盘聚合器（纯函数，可单测）。口径以 ARCHITECTURE.md 第 5 节为准：
///   - 有效专注分钟 = 当日 Focused 驻留段之和；段长 = 下一事件行时间戳 − 本行，
///     尾段无后续行时截止到当日 24:00（兜底）；'AppExit' 等行天然截断尾段；
///   - CountAsValid 纠正时刻起记 Focused 段，单段上限 30 分钟，截到下一事件行；
///   - 最强时段 = 对齐 30 分钟窗内专注分钟最多者，并列取最早；
///   - 明天建议 = 最强窗后第一个 Focused 段起点（分钟数取该段时长，夹紧 5–60），
///     无则默认"开始时先做最难的 12 分钟"。
/// </summary>
public static class DailyReportAggregator
{
    private static readonly TimeSpan PeakWindowLength = TimeSpan.FromMinutes(30);
    private const int AdviceDefaultMinutes = 12;
    private const int AdviceMinMinutes = 5;
    private const int AdviceMaxMinutes = 60;
    private const double CorrectionSegmentCapMinutes = 30;

    public static DailyReportData Aggregate(
        IReadOnlyList<EventRow> events,
        IReadOnlyList<CorrectionRow> corrections,
        DateOnly date,
        TimeZoneInfo timeZone)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        // 事件行 → 本地时间驻留段（每行是其前状态的"段结束时刻"）
        var localTimes = new List<DateTime>(events.Count);
        var segments = new List<(DateTime Start, DateTime End, string State)>();
        DateTime? segStart = null;
        var segState = string.Empty;
        foreach (var row in events)
        {
            var local = TimeZoneInfo.ConvertTime(row.AtUtc, timeZone).DateTime;
            localTimes.Add(local);
            if (segStart is { } start)
            {
                segments.Add((start, local, segState));
            }

            segStart = local;
            segState = row.State;
        }

        // 尾段：当日最后一行之后无行 → 截止到日界（应用退出时的 AppExit 行会走上一分支，不会膨胀到这里）
        if (segStart is { } lastStart)
        {
            segments.Add((lastStart, dayEnd, segState));
        }

        var focusedMinutes = 0d;
        var distractedMinutes = 0d;
        foreach (var (start, end, state) in segments)
        {
            var clampedStart = start < dayStart ? dayStart : start;
            var clampedEnd = end > dayEnd ? dayEnd : end;
            if (clampedEnd <= clampedStart)
            {
                continue;
            }

            var minutes = (clampedEnd - clampedStart).TotalMinutes;
            if (state == nameof(FocusState.Focused))
            {
                focusedMinutes += minutes;
            }
            else if (state == nameof(FocusState.Distracted))
            {
                distractedMinutes += minutes;
            }
        }

        // 纠正段：CountAsValid 时刻起记 Focused（上限 30 分钟，截到下一事件行与日界）
        var validCorrections = 0;
        foreach (var correction in corrections)
        {
            if (correction.Kind != UserCorrectionKind.CountAsValid)
            {
                continue;
            }

            validCorrections++;
            var local = TimeZoneInfo.ConvertTime(correction.AtUtc, timeZone).DateTime;
            var start = local < dayStart ? dayStart : local;
            var endByCap = start.AddMinutes(CorrectionSegmentCapMinutes);
            var endByNextEvent = NextEventAfter(localTimes, local);
            var end = Min(endByCap, endByNextEvent ?? dayEnd, dayEnd);
            if (end > start)
            {
                focusedMinutes += (end - start).TotalMinutes;
            }
        }

        var totalFocused = (int)Math.Round(focusedMinutes);
        var totalDistracted = (int)Math.Round(distractedMinutes);

        // 最强 30 分钟对齐窗
        var peak = FindPeakWindow(segments, dayStart);

        // 明天第一个动作：最强窗后的第一个 Focused 段起点
        TimeOnly? adviceTime = null;
        var adviceMinutes = AdviceDefaultMinutes;
        if (peak is { } peakWindow)
        {
            var peakEnd = dayStart + peakWindow.End;
            foreach (var (start, end, state) in segments)
            {
                if (state != nameof(FocusState.Focused) || start < peakEnd)
                {
                    continue;
                }

                adviceTime = TimeOnly.FromDateTime(start);
                adviceMinutes = Math.Clamp((int)Math.Round((end - start).TotalMinutes),
                    AdviceMinMinutes, AdviceMaxMinutes);
                break;
            }
        }

        return new DailyReportData(
            date,
            totalFocused,
            peak?.Start,
            peak?.End,
            adviceTime,
            adviceMinutes,
            totalDistracted,
            corrections.Count);
    }

    /// <summary>最强 30 分钟对齐窗（当日无 Focused 分钟返回 null；并列取最早）。</summary>
    private static (TimeSpan Start, TimeSpan End)? FindPeakWindow(
        List<(DateTime Start, DateTime End, string State)> segments,
        DateTime dayStart)
    {
        Span<double> buckets = stackalloc double[48];
        foreach (var (start, end, state) in segments)
        {
            if (state != nameof(FocusState.Focused))
            {
                continue;
            }

            var firstBucket = Math.Max(0, (int)((start - dayStart).TotalMinutes / 30));
            var lastBucket = Math.Min(47, (int)Math.Floor(((end - dayStart).TotalMinutes - 0.0001) / 30));
            for (var b = firstBucket; b <= lastBucket && b < 48; b++)
            {
                var bucketStart = dayStart.AddMinutes(b * 30);
                var bucketEnd = bucketStart.AddMinutes(30);
                var overlapStart = start > bucketStart ? start : bucketStart;
                var overlapEnd = end < bucketEnd ? end : bucketEnd;
                if (overlapEnd > overlapStart)
                {
                    buckets[b] += (overlapEnd - overlapStart).TotalMinutes;
                }
            }
        }

        var best = -1;
        for (var b = 0; b < 48; b++)
        {
            if (buckets[b] <= 0)
            {
                continue;
            }

            if (best < 0 || buckets[b] > buckets[best])
            {
                best = b;
            }
        }

        if (best < 0)
        {
            return null;
        }

        return (TimeSpan.FromMinutes(best * 30), TimeSpan.FromMinutes((best + 1) * 30));
    }

    private static DateTime? NextEventAfter(List<DateTime> localTimes, DateTime moment)
    {
        DateTime? next = null;
        foreach (var t in localTimes)
        {
            if (t > moment && (next is null || t < next))
            {
                next = t;
            }
        }

        return next;
    }

    private static DateTime Min(DateTime a, DateTime b, DateTime c) =>
        a < b ? (a < c ? a : c) : (b < c ? b : c);
}
