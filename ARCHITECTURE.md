# 架构说明（ARCHITECTURE）

## 1. 分层与依赖

```
┌─────────────────────────┐         ┌──────────────────────────┐
│       Yaya.App          │         │     Yaya.Core.Tests      │
│  WPF / 托盘 / 主窗口     │         │  xUnit，仅引用 Core       │
│  感知循环（DispatcherTimer│         │  状态机 / 分类器 / DB /   │
│  + Win32 只读采集）       │         │  复盘文案 的行为契约测试   │
└───────────┬─────────────┘         └───────────┬──────────────┘
            │ 项目引用                           │ 项目引用
            ▼                                   ▼
        ┌──────────────────────────────────────────┐
        │               Yaya.Core                  │
        │  纯 C#，TargetFramework=net8.0            │
        │  Monitoring: 三态状态机 / 标题分类器 / 选项 │
        │  Storage:    YayaDb（Microsoft.Data.Sqlite）│
        │  Services:   DailySummaryComposer        │
        └──────────────────────────────────────────┘
```

铁律：Core 不引用 WPF/WinForms/Win32，不启动定时器，不做 IO（除 SQLite）；
一切平台相关代码（托盘、P/Invoke、Dispatcher）只允许存在于 App 层。
Core 的任何行为变更必须伴随 `Yaya.Core.Tests` 中的单元测试。

## 2. 采集边界（隐私红线，不可逾越）

| 采集 | 方式 | 说明 |
| --- | --- | --- |
| 前台窗口标题 | `GetForegroundWindow` + `GetWindowText`（只读） | 不读 URL、不读进程路径、不枚举窗口内容 |
| 键鼠空闲时长 | `GetLastInputInfo` 系统级计数差 | 不记录按键内容、不装键盘钩子 |

**不截屏、不读摄像头、不读麦克风。** 分类只用标题关键词（`AppCategoryClassifier`），数据只写本机 SQLite。

## 3. 三态状态机（`FocusStateMachine` v2，纯逻辑可单测）

状态：`Focused`（有效学习） / `Distracted`（游离） / `Uncertain`（不确定）。
输入：`Observation(AppCategory 类别, long 空闲毫秒数, PomodoroPhase 番茄钟相位)`——Core 只消费抽象信号，不认识具体窗口；时钟使用注入的 `TimeProvider`（测试拨 FakeClock，无 Thread.Sleep）。

判定决策表（`Evaluate` 内按序先命中先生效）：

| # | 条件 | 判定 | 询问 |
| --- | --- | --- | --- |
| R1 | 微重启窗口内 | Uncertain | 不询问 |
| R2 | 前台类别刚切换 < 宽限期 | Uncertain（防 Alt-Tab 路过误判） | 不询问 |
| R3 | Study / Work 且空闲 < 阈值 | Focused | 不询问 |
| R4 | Entertainment / Social 且相位 ≠ Break | Distracted | 新进入时询问* |
| R5 | Study / Work 且空闲 ≥ 阈值（信号冲突） | Uncertain | 不询问 |
| R6 | Break 相位 + 娱乐/社交（合法休息） | Uncertain | 不询问 |
| R7 | 其他（Unknown 等信号不足） | Uncertain | 不询问 |

* 询问门控：`ShouldNudge` 仅当「新进入 Distracted」∧「同类询问距上次 ≥ NudgeCooldownMinutes」。
产品红线"低置信度不打扰" = Uncertain 永不询问 + 微重启窗口静默。v2 起不再使用数值置信度，低置信由 Uncertain 表达。

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
    app_category  TEXT    NOT NULL,     -- Study / Work / Entertainment / Social / Unknown
    session_id    TEXT    NOT NULL,     -- 本次应用运行的会话 ID
    state         TEXT    NOT NULL,     -- 该时刻状态机状态
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
    summary_text       TEXT    NOT NULL,  -- 固定三句话复盘
    created_at_utc     TEXT    NOT NULL
);
```

一键清空 = `DELETE FROM` 三张表 + `VACUUM`，保留表结构（`YayaDb.ClearAllData()`）。

## 5. 每日复盘（产品红线：必须短，三句话）

`DailySummaryComposer.Compose(focused, distracted, correctionCount)` 纯函数生成，固定三句：
1. 事实：「今天有效学习约 X 分钟，游离约 Y 分钟。」
2. 建议：按时长对比二选一（游离偏多→收敛建议；否则→鼓励）。
3. 回执：有纠正→「今天你纠正了我 N 次……」；无纠正→肯定语气。

单测硬约束：任意输入输出必须恰好含 3 个「。」。

## 6. 已知取舍与 TODO

- 前台切换宽限期已实现（`CategorySwitchGraceMilliseconds`）；连续 N 次一致样本才迁移的状态滞回仍是 TODO。
- 分类关键词表为占位；TODO：用户自定义词表 + 从 corrections 学习。
- 番茄钟相位已进入决策表（R6），但 App 侧番茄钟尚未接入，v1 恒传 `None`；TODO(app)。
- 每日复盘生成时机（关窗/23:59/次日首次启动）待产品定；TODO。
- App 单进程单 UI 线程写库；若未来加后台服务再引入锁/WAL。
