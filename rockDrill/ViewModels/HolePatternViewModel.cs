using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;

namespace rockDrill.ViewModels;

/// <summary>单个炮孔（布孔图上的一个点位）。</summary>
public class HoleVm
{
    public int No { get; init; }
    public string NoText => No.ToString();
    public string Type { get; init; } = "辅助孔";

    /// <summary>画布内左上角坐标（已包含半径偏移）</summary>
    public double Left { get; init; }
    public double Top { get; init; }
    public double Size { get; init; } = 18;

    public double CenterX { get; init; }
    public double CenterY { get; init; }

    public IBrush Fill { get; init; } = new SolidColorBrush(Color.Parse("#22323F"));
    public IBrush Stroke { get; init; } = new SolidColorBrush(Color.Parse("#3A5468"));
    public double StrokeThickness { get; init; } = 1;

    public string StateText { get; init; } = "待钻";
    public IBrush StateBrush { get; init; } = new SolidColorBrush(Color.Parse("#5C7484"));
    public bool IsFault { get; init; }

    public string XText { get; init; } = "0.00";
    public string YText { get; init; } = "0.00";
    public string DepthText { get; init; } = "0.00";
    public string AngleText { get; init; } = "90.0";
    public string BoomText { get; init; } = "左臂";
}

/// <summary>炮孔类型统计。</summary>
public class HoleTypeStatVm
{
    public string Name { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public double Percent { get; init; }
    public IBrush Brush { get; init; } = Brushes.DeepSkyBlue;
}

/// <summary>布孔图：掌子面炮孔布置、进度与单孔信息。</summary>
public partial class HolePatternViewModel : ViewModelBase
{
    public const double CanvasWidth = 800;
    public const double CanvasHeight = 560;

    public ObservableCollection<HoleVm> Holes { get; } = new();
    public ObservableCollection<HoleTypeStatVm> TypeStats { get; } = new();

    public string SectionText => "12.40 × 9.20 m";
    public string AdvanceText => "3.50 m";
    public string DiameterText => "φ45 mm";
    public string PowderText => "118 kg";

    public int DoneCount => Holes.Count(h => h.StateText == "已完成");
    public int FaultCount => Holes.Count(h => h.IsFault);
    public int RemainCount => Holes.Count - DoneCount;
    public double DonePercent => Holes.Count == 0 ? 0 : (double)DoneCount / Holes.Count * 100;

    public string DoneText => $"{DoneCount} / {Holes.Count}";
    public string RemainText => $"{RemainCount} 个";

    /// <summary>当前选中的孔（正在钻进的那个）。</summary>
    public HoleVm? SelectedHole { get; private set; }

    public string SelectedNoText => SelectedHole is null ? "--" : $"No.{SelectedHole.No}";
    public string SelectedTypeText => SelectedHole?.Type ?? "--";
    public string SelectedXText => SelectedHole?.XText ?? "--";
    public string SelectedYText => SelectedHole?.YText ?? "--";
    public string SelectedDepthText => SelectedHole?.DepthText ?? "--";
    public string SelectedAngleText => SelectedHole?.AngleText ?? "--";
    public string SelectedBoomText => SelectedHole?.BoomText ?? "--";
    public string SelectedStateText => SelectedHole?.StateText ?? "--";
    public IBrush SelectedStateBrush => SelectedHole?.StateBrush ?? Brushes.Gray;

    public HolePatternViewModel()
    {
        BuildPattern();
    }

    private void BuildPattern()
    {
        Holes.Clear();

        const int cols = 12;
        const int rows = 8;
        const double spacingX = 56;
        const double spacingY = 60;
        const double originX = 80;
        const double originY = 70;

        var cx = CanvasWidth / 2;
        var cy = CanvasHeight / 2;

        // 1) 生成点位（交错排布，模拟真实布孔）
        var points = new List<(double x, double y, int r, int c)>();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var x = originX + c * spacingX + (r % 2 == 0 ? 0 : spacingX / 2);
                var y = originY + r * spacingY;
                if (x > CanvasWidth - 55) continue;
                points.Add((x, y, r, c));
            }
        }

        // 2) 分类：最内 8 个为掏槽孔，最外一圈为周边孔，最底排为底板孔
        var centerEight = points
            .OrderBy(p => (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy))
            .Take(8)
            .ToHashSet();

        string TypeOf((double x, double y, int r, int c) p)
        {
            if (centerEight.Contains(p)) return "掏槽孔";
            if (p.r == rows - 1) return "底板孔";
            if (p.r == 0 || p.c == 0 || p.c == cols - 1) return "周边孔";
            return "辅助孔";
        }

        // 3) 编号顺序：掏槽 → 辅助 → 周边 → 底板
        var ordered = points
            .OrderBy(p => TypeOf(p) switch
            {
                "掏槽孔" => 0,
                "辅助孔" => 1,
                "周边孔" => 2,
                _ => 3
            })
            .ThenBy(p => (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy))
            .ThenBy(p => p.y)
            .ToList();

        var total = ordered.Count;
        var done = 61;
        var drillingNo = done + 1;
        var faultNo = 88;

        var i = 0;
        foreach (var p in ordered)
        {
            i++;
            var type = TypeOf(p);

            string state;
            IBrush fill;
            IBrush stroke;
            IBrush stateBrush;
            double thickness;
            var isFault = false;

            if (i <= done)
            {
                state = "已完成";
                fill = new SolidColorBrush(Color.Parse("#15402F"));
                stroke = new SolidColorBrush(Color.Parse("#2AD07C"));
                stateBrush = new SolidColorBrush(Color.Parse("#2AD07C"));
                thickness = 1;
            }
            else if (i == drillingNo)
            {
                state = "钻进中";
                fill = new SolidColorBrush(Color.Parse("#4A3612"));
                stroke = new SolidColorBrush(Color.Parse("#F5A623"));
                stateBrush = new SolidColorBrush(Color.Parse("#F5A623"));
                thickness = 2;
            }
            else if (i == faultNo)
            {
                state = "异常";
                fill = new SolidColorBrush(Color.Parse("#42151B"));
                stroke = new SolidColorBrush(Color.Parse("#ED4C5C"));
                stateBrush = new SolidColorBrush(Color.Parse("#ED4C5C"));
                thickness = 2;
                isFault = true;
            }
            else
            {
                state = "待钻";
                fill = new SolidColorBrush(Color.Parse("#16242F"));
                stroke = new SolidColorBrush(Color.Parse("#33506A"));
                stateBrush = new SolidColorBrush(Color.Parse("#5C7484"));
                thickness = 1;
            }

            const double size = 17;
            var hole = new HoleVm
            {
                No = i,
                Type = type,
                CenterX = p.x,
                CenterY = p.y,
                Left = p.x - size / 2,
                Top = p.y - size / 2,
                Size = size,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StateText = state,
                StateBrush = stateBrush,
                IsFault = isFault,
                XText = ((p.x - cx) / 55).ToString("F2"),
                YText = ((cy - p.y) / 55).ToString("F2"),
                DepthText = i <= done ? "3.50" : i == drillingNo ? "1.84" : "0.00",
                AngleText = (90 + (p.x - cx) / 45).ToString("F1"),
                BoomText = p.x < cx ? "左臂" : "右臂"
            };

            Holes.Add(hole);
            if (i == drillingNo) SelectedHole = hole;
        }

        BuildStats();
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(FaultCount));
        OnPropertyChanged(nameof(RemainCount));
        OnPropertyChanged(nameof(DonePercent));
        OnPropertyChanged(nameof(DoneText));
        OnPropertyChanged(nameof(RemainText));
    }

    private void BuildStats()
    {
        TypeStats.Clear();
        var groups = Holes.GroupBy(h => h.Type)
            .OrderBy(g => g.Key switch
            {
                "掏槽孔" => 0,
                "辅助孔" => 1,
                "周边孔" => 2,
                _ => 3
            });

        foreach (var g in groups)
        {
            var brush = g.Key switch
            {
                "掏槽孔" => Brushes.DeepSkyBlue,
                "辅助孔" => Brushes.MediumPurple,
                "周边孔" => Brushes.MediumTurquoise,
                _ => Brushes.Goldenrod
            };
            var done = g.Count(h => h.StateText == "已完成");
            TypeStats.Add(new HoleTypeStatVm
            {
                Name = g.Key,
                CountText = $"{done} / {g.Count()}",
                Percent = g.Count() == 0 ? 0 : (double)done / g.Count() * 100,
                Brush = brush
            });
        }
    }
}
