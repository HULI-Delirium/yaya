# 工程约定（CONVENTIONS）

## 1. 命名规范

| 目标 | 规则 | 示例 |
| --- | --- | --- |
| 命名空间 / 类 / 公开成员 | PascalCase，与文件同名 | `FocusStateMachine.cs` → `class FocusStateMachine` |
| 私有字段 | `_camelCase` | `private DateTimeOffset? _lastNotifiedAt;` |
| 接口 | `I` 前缀 | `IProbe`（如未来引入） |
| 常量 | PascalCase，见名知义 | `IdleThresholdMilliseconds` |
| 测试类 | `<被测类>Tests` | `FocusStateMachineTests` |
| 测试方法 | `被测方法_场景_预期`（中文允许） | `Evaluate_冷却期内再次游离_不打扰` |

## 2. 注释规范（中文）

- 注释用简体中文；XML 文档注释写在公开 API 上，说明"做什么/红线是什么"，不复述代码。
- 阈值、边界、隐私红线必须注释并带 `TODO(校准)` 或指向 ARCHITECTURE.md 章节。
- 禁止注释掉的死代码；禁止"// 修复 bug"式考古注释（历史交给 git blame）。

## 3. Commit 格式（Conventional Commits）

```
<type>(<scope>): <一句话概述>（中文，祈使句，≤50 字）

[可选正文：为什么这么做，而非做了什么]
```

type：`feat` / `fix` / `test` / `docs` / `refactor` / `chore` / `perf`
scope：`core` / `app` / `tests` / `db` / `build` / `docs`

示例：

```
feat(core): 新增三态状态机与规则决策表
fix(app): 托盘菜单一键清空后补记审计日志
test(core): 补充低置信不打扰与冷却恢复用例
docs(architecture): 同步 daily_summary 表结构变更
```

规则：一次提交只做一件事；禁止 `update`、`fix bug`、`misc` 等空泛 type。

## 4. 硬性门禁：Core 层改动必须配单元测试

- `src/Yaya.Core/` 的任何行为变更（新增 public API、改判定规则、改 SQL）必须附带或更新 `tests/Yaya.Core.Tests/` 用例。
- 提交通道：`dotnet test` 全绿才算完；红灯提交视为破坏构建。
- 无法单测的代码（如 P/Invoke、托盘）不属于 Core，放在 App 层并尽量薄。

## 5. 其他

- 分支：`main` 受保护，功能走 `feat/<短横线描述>`；合并前 rebase 保持线性。
- 隐私红线代码（采集边界）改动需正文说明"采集了什么、没采集什么"。
- 日志用语对同学友好，不复读窗口标题全文进提醒气泡（只进本地日志）。
