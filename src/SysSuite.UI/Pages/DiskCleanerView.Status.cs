using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

public partial class DiskCleanerView
{
    private void SetStatus(string text)
    {
        StatusText.Inlines.Clear();
        AddStatusRun(text, "App.SubtleText");
    }

    private void ShowReportStatus(DiskInspectionReport report)
    {
        StatusText.Inlines.Clear();
        AddStatusRun("检查完成：", "App.SubtleText");
        AddStatusRun($"临时文件 {report.Items.Count(item => item.Category == "临时文件")}", "App.SafeText");
        AddStatusRun("，", "App.SubtleText");
        AddStatusRun($"重复文件 {report.Items.Count(item => item.Category == "重复文件")}", "App.CautionText");
        AddStatusRun("，", "App.SubtleText");
        AddStatusRun($"空文件夹 {report.Items.Count(item => item.Category == "空文件夹")}", "App.RiskyText");
        AddStatusRun("，", "App.SubtleText");
        AddStatusRun($"可释放 {DiskCleanerFormat.Bytes(report.Items.Sum(item => item.SizeBytes))}。", "App.Accent");
    }

    private void AddStatusRun(string text, string resourceKey)
    {
        StatusText.Inlines.Add(new Run(text)
        {
            Foreground = TryFindResource(resourceKey) as Brush ?? Foreground
        });
    }
}
