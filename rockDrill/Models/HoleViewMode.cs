namespace rockDrill.Models;

/// <summary>
/// 掌子面视图投影方式，对应现场 HMI 钻孔页左侧的四个视图按钮：
/// 正视图 / 侧视图 / 俯视图 / 三维视图。
/// </summary>
public enum HoleViewMode
{
    /// <summary>正视图：横向 X × 竖向 Z（掌子面正视）。</summary>
    Front,

    /// <summary>侧视图：孔轴进深 Y × 竖向 Z。</summary>
    Side,

    /// <summary>俯视图：横向 X × 孔轴进深 Y。</summary>
    Top,

    /// <summary>三维视图：等轴测投影（X / Y / Z 三向同时可见）。</summary>
    Iso
}

/// <summary>视图名称与说明。</summary>
public static class HoleViewModeInfo
{
    /// <summary>按钮上的中文名称。</summary>
    public static string Display(this HoleViewMode mode) => mode switch
    {
        HoleViewMode.Side => "侧视图",
        HoleViewMode.Top => "俯视图",
        HoleViewMode.Iso => "三维视图",
        _ => "正视图"
    };

    /// <summary>坐标轴说明（画布左下角提示用）。</summary>
    public static string AxisHint(this HoleViewMode mode) => mode switch
    {
        HoleViewMode.Side => "水平：进深 m　垂直：标高 m",
        HoleViewMode.Top => "水平：横向 m　垂直：进深 m",
        HoleViewMode.Iso => "等轴测：横向 / 进深 / 标高",
        _ => "水平：横向 m　垂直：标高 m"
    };

    /// <summary>四个视图按钮的顺序（固定顺序，索引与界面按钮一一对应）。</summary>
    public static readonly HoleViewMode[] All =
    {
        HoleViewMode.Front, HoleViewMode.Side, HoleViewMode.Top, HoleViewMode.Iso
    };
}
