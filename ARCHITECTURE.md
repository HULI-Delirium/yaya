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
        │  Monitoring: 三态状态机 / 标题分类器 / 阈值 │
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

## 3. 三态状态机（`FocusStateMachine`，纯逻辑可单测）

状态：`Focused`（有效学习） / `Distracted`（游离） / `Uncertain`（不确定）。

| 输入样本 | 判定 | 置信度 |
| --- | --- | --- |
| 键鼠空闲 ≥ `IdleThresholdSeconds` | Distracted | 1.0 |
| 标题类别 = Study / Work | Focused | 0.9 |
| 标题类别 = Entertainment / Social | Distracted | 0.9 |
| 其他（含空标题） | Uncertain | 0.3 |

**打扰门控（产品红线：低打扰）**——`ShouldNotify` 仅当同时满足：
1. 本轮**新进入** Distracted（状态未变不重复提醒）；
2. `置信度 ≥ NotifyMinConfidence`（低置信度绝不打扰）；
3. 距上次提醒超过 `NudgeCooldownMinutes`（防连环弹窗）。

打扰形式为托盘气泡，频率与语气以"同伴轻拍"为标准。**用户可纠正**（主窗体按钮或托盘）：纠正写入 `corrections` 表并即时重置当前状态。

### 阈值常量（全部占位，标 TODO 待校准）

集中在 `Monitoring/MonitoringThresholds.cs`：

```csharp
public const double SampleIntervalSeconds = 5;      // TODO(校准)：采样间隔
public const double IdleThresholdSeconds  = 120;    // TODO(校准)：空闲阈值
public const double NotifyMinConfidence   = 0.75;   // TODO(校准)：打扰最低置信度
public const double NudgeCooldownMinutes  = 20;     // TODO(校准)：打扰冷却
```

校准流程：改常量 → `dotnet test` 全绿 → 真实场景观察一周 → 提交（conventional commits）。

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
    corrected_state TEXT    NOT NULL,   -- 用户纠正后的状态
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

- 无状态滞回（防抖），v1 靠采样间隔与冷却兜底；TODO：连续 N 次一致样本才迁移。
- 分类关键词表为占位；TODO：用户自定义词表 + 从 corrections 学习。
- 每日复盘生成时机（关窗/23:59/次日首次启动）待产品定；TODO。
- App 单进程单 UI 线程写库；若未来加后台服务再引入锁/WAL。
