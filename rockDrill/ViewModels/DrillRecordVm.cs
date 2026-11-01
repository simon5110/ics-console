using Avalonia.Media;

namespace rockDrill.ViewModels;

/// <summary>单孔钻孔记录。</summary>
public class DrillRecordVm
{
    public string No { get; init; } = string.Empty;
    public string Depth { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public IBrush Brush { get; init; } = Brushes.YellowGreen;
}
