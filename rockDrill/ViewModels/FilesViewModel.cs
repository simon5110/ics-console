using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>资料文件。</summary>
public class FileItemVm
{
    public string Index { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Kind { get; init; } = "PDF";
}

/// <summary>文件资料：手册、图纸与施工文件预览。</summary>
public partial class FilesViewModel : ViewModelBase
{
    [ObservableProperty] private FileItemVm? _selectedFile;
    [ObservableProperty] private string _previewTitle = "TPC-E156-5128-2N6C16IO 规格书";
    [ObservableProperty] private string _previewSub = "控制器 I/O 模块技术手册 · 版本 V1.4";
    [ObservableProperty] private string _pageText = "第 1 页 / 共 12 页";
    [ObservableProperty] private string _hintText = "选择左侧文件可在线预览，支持导出到 U 盘。";

    public ObservableCollection<FileItemVm> Files { get; } = new();

    public FilesViewModel()
    {
        Files.Add(new FileItemVm { Index = "1", Name = "TPC-E156-5128-2N6C16IO 规格书.pdf", Size = "2.4 MB", Date = "2026-08-12" });
        Files.Add(new FileItemVm { Index = "2", Name = "DW2-100 操作维护手册.pdf", Size = "18.6 MB", Date = "2026-06-30" });
        Files.Add(new FileItemVm { Index = "3", Name = "凿岩台车电气原理图.pdf", Size = "6.1 MB", Date = "2026-05-18" });
        Files.Add(new FileItemVm { Index = "4", Name = "液压系统原理图.pdf", Size = "4.8 MB", Date = "2026-05-18" });
        Files.Add(new FileItemVm { Index = "5", Name = "布孔设计图 K2+186.5.pdf", Size = "0.9 MB", Date = "2026-10-31" });
        Files.Add(new FileItemVm { Index = "6", Name = "班前安全检查表.pdf", Size = "0.3 MB", Date = "2026-10-31" });

        SelectedFile = Files[0];
    }

    partial void OnSelectedFileChanged(FileItemVm? value)
    {
        if (value is null) return;
        PreviewTitle = value.Name.Replace(".pdf", string.Empty);
        PreviewSub = $"{value.Kind} 文档 · {value.Size} · 更新于 {value.Date}";
        HintText = $"正在预览：{value.Name}";
    }

    [RelayCommand]
    private void Export() => HintText = "文件已复制到 U 盘根目录 /DOCS。";
}
