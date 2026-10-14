# 芽芽（Yaya）

> 基于行为感知的 Windows 桌面陪伴宠物，帮大学生维持学习节律——只看「前台进程名 / 窗口标题 + 键鼠空闲时间」，只做本地判断，只给低打扰提醒。

## 三大红线

| 红线 | 含义 |
| --- | --- |
| **不监控** | 不采集屏幕内容、摄像头、麦克风；唯一输入是前台窗口标题文本、前台进程名与系统级键鼠空闲时长 |
| **不诊断** | 不做任何医学 / 心理学 / 学情诊断，提醒与复盘仅供参考 |
| **本地优先，可一键清空** | 数据只写入本机 SQLite，不上传、不同步；托盘菜单「一键清空本地数据」一键删空 |

## 构建与运行

前置：.NET SDK ≥ 8（装 .NET 8 SDK 最省心；只装 .NET 9 SDK + .NET 8 运行时也能构建运行）。

```bash
dotnet restore                       # 首次还原依赖（网络抖动失败时重试即可）
dotnet build                         # 编译全部三个项目
dotnet test                          # 运行 Core 层单元测试（改动 Core 必须全绿）
dotnet run --project src/Yaya.App    # 启动：托盘图标 + 主窗口
```

发布后运行：`dotnet publish src/Yaya.App -c Release -r win-x64 --self-contained true`（可选）。

## 目录结构

```
Yaya.sln
src/
  Yaya.App/          # WPF UI 层：系统托盘、主窗口、感知循环、Win32 只读采集
  Yaya.Core/         # 领域逻辑：三态状态机、进程名映射、SQLite 存储、复盘文案（零 UI 依赖）
tests/
  Yaya.Core.Tests/   # xUnit 单元测试，仅引用 Core
```

依赖方向：`Yaya.App → Yaya.Core ← Yaya.Core.Tests`，Core 不引用任何 UI 框架。

## 数据与隐私

- 数据库：`%LOCALAPPDATA%\Yaya\yaya.db`（SQLite，三张表：`events` / `corrections` / `daily_summary`）
- 日志：`%LOCALAPPDATA%\Yaya\logs\yaya-*.log`（按天滚动，保留 7 天）
- 彻底移除：退出程序后删除上述整个 `Yaya` 目录即可，无任何远端副本

## 免责声明

芽芽不是医疗、心理诊断或学情评估工具；所有判断仅基于本机粗粒度行为信号，复盘固定三句话，拒绝制造焦虑。
