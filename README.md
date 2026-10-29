# 芽芽（Yaya）

> 基于行为感知的 Windows 桌面陪伴宠物，帮大学生维持学习节律——只看「前台进程名 / 窗口标题 + 键鼠空闲时间」，只做本地判断，只给低打扰提醒。

## 三大红线

| 红线 | 含义 |
| --- | --- |
| **不监控** | 不采集屏幕内容、摄像头、麦克风；唯一输入是前台窗口标题文本、前台进程名与系统级键鼠空闲时长 |
| **不诊断** | 不做任何医学 / 心理学 / 学情诊断，提醒与复盘仅供参考 |
| **本地优先，可一键清空** | 数据只写入本机 SQLite，不上传、不同步；托盘菜单「一键清空本地数据」一键删空 |

## 构建与运行

前置：.NET SDK ≥ 8（仓库 `global.json` 固定 8.0.x，`rollForward=latestFeature`，会选用已安装的最新 8.0 特性带）

```bash
dotnet restore                       # 首次还原依赖（网络抖动失败时重试即可）
dotnet build                         # 编译全部三个项目
dotnet test                          # 运行 Core 层单元测试（改动 Core 必须全绿）
dotnet run --project src/Yaya.App    # 启动：托盘图标 + 主窗口
```

## 发布（单文件 exe）

```bash
dotnet publish src/Yaya.App -p:PublishProfile=win-x64
# 产物：src/Yaya.App/bin/Release/net8.0-windows/win-x64/publish/Yaya.App.exe
# 约 75MB，自包含（win-x64），干净 Windows 机器无需安装 .NET 即可运行
```

分发只需拷贝 `Yaya.App.exe` 一个文件。注意事项：

- 6 张宠物图**内嵌**在 exe 内；要换图，在 exe 旁建立 `Assets/pet/` 目录放入同名 PNG 即可覆盖（加载优先级：磁盘 > 内嵌 > 矢量占位）。
- 发布目录可能附带 `Yaya.Core.pdb`（引用项目的符号文件），分发时忽略即可。
- 数据、日志与设置仍写在 `%LOCALAPPDATA%\Yaya\`，exe 所在目录保持只读也能正常运行。
- 首次发布演练记录见 [docs/release-drill-2026-10.md](docs/release-drill-2026-10.md)。

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
- 分类规则：`%LOCALAPPDATA%\Yaya\rules.json`（只存用户输入的进程名/标题关键词；不读 URL、不读页面内容）
- 日志：`%LOCALAPPDATA%\Yaya\logs\yaya-*.log`（按天滚动，保留 7 天；窗口标题默认不写，
  仅在设置里显式开启「诊断日志」时记录）
- 彻底移除：退出程序后删除上述整个 `Yaya` 目录即可，无任何远端副本

## 免责声明

芽芽不是医疗、心理诊断或学情评估工具；所有判断仅基于本机粗粒度行为信号，拒绝制造焦虑。
