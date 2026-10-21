# 架构说明（ARCHITECTURE）

## 1. 分层与依赖

```
┌─────────────────────────┐         ┌──────────────────────────┐
│       Yaya.App          │         │     Yaya.Core.Tests      │
│  WPF / 托盘 / 宠物窗     │         │  xUnit，仅引用 Core       │
│  采集循环 + 番茄钟服务    │         │  状态机 / 映射表 / 聚合 / │
│  （DispatcherTimer）     │         │  DB 的行为契约测试        │
└───────────┬─────────────┘         └───────────┬──────────────┘
            │ 项目引用                           │ 项目引用
            ▼                                   ▼
        ┌──────────────────────────────────────────┐
        │               Yaya.Core                  │
        │  纯 C#，TargetFramework=net8.0            │
        │  Monitoring: 三态状态机 / 进程名映射 / 选项 │
        │  Storage:    YayaDb（Microsoft.Data.Sqlite）│
        │  Services:   复盘聚合器 / IReportWriter   │
        └──────────────────────────────────────────┘
```

铁律：Core 不引用 WPF/WinForms/Win32，不启动定时器，不做 IO（除 SQLite）；
一切平台相关代码（托盘、P/Invoke、Dispatcher）只允许存在于 App 层。
Core 的任何行为变更必须伴随 `Yaya.Core.Tests` 中的单元测试。

## 2. 采集边界（隐私红线，不可逾越）

| 采集 | 方式 | 说明 |
| --- | --- | --- |
| 前台窗口标题 | `GetForegroundWindow` + `GetWindowText`（只读） | 不读 URL、不读进程路径、不枚举窗口内容 |
| 前台进程名 | `GetWindowThreadProcessId` → `Process.ProcessName`（只读元数据） | 仅用于类别映射，不枚举进程、不读模块/路径 |
| 键鼠空闲时长 | `GetLastInputInfo` 系统级计数差 | 不记录按键内容、不装键盘钩子 |

**不截屏、不读摄像头、不读麦克风。** 分类用进程名映射（`AppCatalog`），浏览器等宿主进程退回标题关键词兜底；数据只写本机 SQLite。

## 3. 三态状态机（`FocusStateMachine` v2，纯逻辑可单测）

状态：`Focused`（有效学习） / `Distracted`（游离） / `Uncertain`（不确定）。
输入：`Observation(AppCategory 类别, long 空闲毫秒数, PomodoroPhase 番茄钟相位)`——Core 只消费抽象信号，不认识具体窗口；时钟使用注入的 `TimeProvider`（测试拨 FakeClock，无 Thread.Sleep）。

判定决策表（`Evaluate` 内按序先命中先生效）：

| # | 条件 | 判定 | 询问 |
| --- | --- | --- | --- |
| R1 | 微重启窗口内 | Uncertain | 不询问 |
| R2 | 前台类别刚切换 < 宽限期 | Uncertain（防 Alt-Tab 路过误判） | 不询问 |
| R3 | Study / Utility 且空闲 < 阈值 | Focused | 不询问 |
| R4 | Entertainment / Social 且相位 ≠ Break | Distracted | 新进入时询问* |
| R5 | Study / Utility 且空闲 ≥ 阈值（信号冲突） | Uncertain | 不询问 |
| R6 | Break 相位 + 娱乐/社交（合法休息） | Uncertain | 不询问 |
| R7 | 其他（Unknown 等信号不足） | Uncertain | 不询问 |

* 询问门控：`ShouldNudge` 仅当「新进入 Distracted」∧「同类询问距上次 ≥ NudgeCooldownMinutes」。
产品红线"低置信度不打扰" = Uncertain 永不询问 + 微重启窗口静默。v2 起不再使用数值置信度，低置信由 Uncertain 表达。

**番茄钟相位**由 App 的 `PomodoroService` 动态供给（`Func<PomodoroPhase>` 注入 `MonitoringLoop`，未开启时恒为 `None`）。Break 相位内娱乐/社交合法（R6），相位翻转带来两条迁移路径：**游离中进入休息 → Uncertain（翻案、停止打扰）**；**休息结束回专注相位 → 恢复正常判定（同类冷却照常生效）**。

阈值全部集中在 `FocusStateMachineOptions`（可配置，set 时校验 > 0，非法值立即抛异常）：

```csharp
IdleThresholdMilliseconds       = 120_000   // TODO(校准)：空闲阈值（学习窗口超时视为冲突）
CategorySwitchGraceMilliseconds = 8_000     // TODO(校准)：前台切换宽限期
NudgeCooldownMinutes            = 10        // TODO(校准)：同类询问冷却
MicroRestartMinutes             = 2         // TODO(校准)：逃避后的微重启窗口
```

校准流程：改默认值或注入新 Options → `dotnet test` 全绿 → 真实场景观察一周 → 提交（conventional commits）。

用户纠正（均返回 `CorrectionRecord` 纯数据，由调用方落库 corrections 表，状态机保持零 IO）：

- `MarkAsValid`（刚才算有效）→ 回退 `Focused`，并结束微重启窗口；
- `MarkEscape`（逃避）→ 进入微重启窗口（`InMicroRestart` 可供 UI 显示"重新开始"），窗口内不询问。

输出：`Evaluate` 返回 `FocusTransition(From, To, ShouldNudge, NudgeKind?)`；`StateChanged` 事件供 UI 播放动画。

## 4. SQLite 三张表（草案 v1）

结构事实来源是 `YayaDb.SchemaSql`；改表必须同步此文档并跑通 `YayaDbTests`。
约定：时间戳存 ISO 8601 UTC 字符串；复盘按本地日期一行；会话 ID = 每次应用启动生成的 Guid（N 格式）。

```sql
CREATE TABLE IF NOT EXISTS events (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc TEXT    NOT NULL,     -- 观测时刻（ISO 8601 UTC）
    app_category  TEXT    NOT NULL,     -- Study / Utility / Entertainment / Social / Unknown
    session_id    TEXT    NOT NULL,     -- 本次应用运行的会话 ID
    state         TEXT    NOT NULL,     -- Focused/Distracted/Uncertain 迁移；
                                        -- 'AppExit'（退出事件行，截断复盘尾段）；
                                        -- 'PomodoroFocusCompleted'/'PomodoroBreakCompleted'/'PomodoroAborted'
    idle_seconds  REAL    NOT NULL      -- 当时键鼠空闲秒数
);
CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp_utc);
CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);

CREATE TABLE IF NOT EXISTS corrections (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc   TEXT    NOT NULL,
    original_state  TEXT    NOT NULL,   -- 芽芽的原判断
    corrected_state TEXT    NOT NULL,   -- 纠正后的状态
    kind            TEXT    NOT NULL,   -- CountAsValid / Escape（UserCorrectionKind）
    app_category    TEXT    NOT NULL,   -- 被纠正时的标题类别
    note            TEXT                -- 用户备注（可空）
);

CREATE TABLE IF NOT EXISTS daily_summary (
    date               TEXT PRIMARY KEY,  -- 本地日期 yyyy-MM-dd
    focused_minutes    INTEGER NOT NULL DEFAULT 0,
    distracted_minutes INTEGER NOT NULL DEFAULT 0,
    correction_count   INTEGER NOT NULL DEFAULT 0,
    summary_text       TEXT    NOT NULL,  -- 三句话展示文本（IReportWriter 产出）
    created_at_utc     TEXT    NOT NULL,
    report_json        TEXT               -- DailyReportData 序列化（各版写出器的稳定输入）
);
```

一键清空 = `DELETE FROM` 三张表 + `VACUUM`，保留表结构（`YayaDb.ClearAllData()`）。

## 5. 每日复盘（产品红线：必须短，三句话）

打开入口：**每天首次启动**（当日报告已生成则安静不弹）与托盘"今日复盘"。
缓存语义：当天已缓存（`report_json` 非空且可解析）直接读，**同一天不重复聚合**；当日 24 点后的新 events 归次日。

聚合口径（`DailyReportAggregator`，纯函数；`GetEventsForDate` 含次日首行，时区参数化便于测试）：

- **有效专注分钟**：本地日界 00:00–24:00 内 `Focused` 驻留段之和。段长 = 下一事件行时间戳 − 本行；**退出事件行（`AppExit`，MonitoringLoop 退出时写入）截断尾段**，关机后的时间不会算进专注；尾段无后续行时按兜底截止到当日 24:00（应用仍在运行）；跨零点段按 00:00 切开各归各日。
- **纠正口径**：`corrections` 中 `CountAsValid` 时刻起记 Focused 段（截到下一事件行，**单段封顶 30 分钟**，防手滑刷数）；`Escape` 不反向扣分；`correction_count` 计当日全部纠正。
- **最强时段**：对齐 30 分钟窗（00:00–00:30、00:30–01:00…）内专注分钟最多者，**并列取最早**；当日无专注为 null。
- **明天建议**：最强窗后的第一个 Focused 段起点 T（建议分钟 = 该段时长，夹紧 5–60）；找不到则默认"开始时先做最难的 12 分钟"（T=null，12 分钟）。

展示链：`DailyReportData`（结构化聚合结果）→ `IReportWriter.Write` → 恰好三句（硬约束测试）。
本周实现 `RuleReportWriter`（规则模板）；**第 3 周 LLM 版实现同一接口替换即可，聚合与 UI 均不感知**。
结构化结果以 JSON 落 `daily_summary.report_json`，是规则版与 LLM 版共同的稳定输入。

## 6. 资源红线与已知取舍

资源红线（实测验收见交付清单）：常驻内存 ≤ 300MB、CPU ≤ 2%。达标设计：
采集循环为 2 秒一次的 DispatcherTimer（UI 线程），单轮仅数次 µs 级 P/Invoke + 字典查找 + 状态机求值；
events/corrections 只在状态迁移、纠正与番茄完成/中断时写入（稳态零 I/O）；
无增长型内存缓存；`MonitoringLoop.Dispose`（写退出事件行）停表退订，App 退出依序释放。

**NFR 已在参考硬件上验证通过**（2026-10 发布演练）：AMD R7 9700X / Win11 专业版 / 32GB，
单文件 exe 纯挂机 30 分钟：CPU 均值 0.58%（≤2%）、私有内存均值 140MB、工作集峰值 255.9MB（≤300MB）。
数据出处与证据链：`docs/release-drill-2026-10.md` §5（双机对照，判定以常规配置机器为准；
老核显机器因 WPF 动画软渲染出现 9.47% 属硬件代差，非代码缺陷，优化决策已取消）。

- 前台切换宽限期已实现（`CategorySwitchGraceMilliseconds`）；连续 N 次一致样本才迁移的状态滞回仍是 TODO。
- 进程名映射表与浏览器标题兜底已实现（`AppCatalog`）；词表为占位，TODO：用户自定义词表 + 从 corrections 学习。
- 番茄钟已接入（`PomodoroService` → 相位注入）；时长设置存 settings.json；「明天开始」为单次覆盖不回写默认。
- 复盘当日缓存冻结；若未来需要"当日二次聚合"，再加失效策略（TODO）。
- App 单进程单 UI 线程写库；若未来 tick 变重或加后台服务，再评估 PeriodicTimer + WAL。
