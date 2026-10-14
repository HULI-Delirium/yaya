namespace Yaya.Core.Monitoring;

/// <summary>
/// 前台应用类别。由 App 层通过 AppCatalog（进程名映射 + 浏览器标题兜底）解析后传入，
/// Core 不认识具体进程（隐私红线见 ARCHITECTURE.md 第 2 节）。
/// </summary>
public enum AppCategory
{
    /// <summary>学习类：文档、课件、笔记、编程 IDE 等。</summary>
    Study = 0,

    /// <summary>工具类：中性生产力工具（计算器、资源管理器等），判定上并入学习侧。</summary>
    Utility = 1,

    /// <summary>娱乐类：游戏、视频、音乐等。</summary>
    Entertainment = 2,

    /// <summary>社交类：IM、社交网页等。</summary>
    Social = 3,

    /// <summary>未知：映射表与标题兜底都无法识别，一律按 Uncertain 处理。</summary>
    Unknown = 4,
}
