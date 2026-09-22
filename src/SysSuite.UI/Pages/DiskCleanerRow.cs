using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

internal static class DiskCleanerFormat
{
    internal static string Bytes(long value) => value switch
    {
        >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d / 1024d:N1} GB"),
        >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d:N1} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d:N1} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{value:N0} B")
    };
}

public sealed class DriveOptionRow : INotifyPropertyChanged
{
    private bool isChecked;

    public DriveOptionRow(DriveOption option)
    {
        Option = option;
        isChecked = option.IsReady && !option.IsSystem;
        Title = string.Create(CultureInfo.InvariantCulture, $"{option.Name} 可用 {DiskCleanerFormat.Bytes(option.FreeBytes)}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DriveOption Option { get; }

    public string Title { get; }

    public bool CanSelect => Option.IsReady;

    public bool IsChecked
    {
        get => isChecked;
        set => SetField(ref isChecked, value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    internal void SetCheckedSilently(bool value) => isChecked = value;
}

public sealed class DiskCleanerRow : INotifyPropertyChanged
{
    private bool isChecked;

    public DiskCleanerRow(DiskCleanItem item)
    {
        Item = item;
        isChecked = false;
        CanSelect = item.Risk != CleanRisk.Risky;
        DriveBadge = System.IO.Path.GetPathRoot(item.Path)?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "磁盘";
        RiskName = item.Risk.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DiskCleanItem Item { get; }

    public DiskGroup Group => Item.Group;

    public DiskGroup GroupKey => Item.Group;

    public string GroupTitle => Item.Group.Title;

    public string Path => Item.Path;

    public string Category => Item.Category;

    public string SizeText => DiskCleanerFormat.Bytes(Item.SizeBytes);

    public string Importance => Item.Importance;

    public string RiskTitle => Item.Risk switch
    {
        CleanRisk.Safe => "安全",
        CleanRisk.Caution => "需注意",
        _ => "高风险"
    };

    public string RiskName { get; }

    public string DriveBadge { get; }

    public bool CanSelect { get; }

    public bool IsChecked
    {
        get => isChecked;
        set => SetField(ref isChecked, value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    internal void SetCheckedSilently(bool value) => isChecked = value;
}

public sealed class DiskCategoryRow
{
    public DiskCategoryRow(string category, string title, int count, long sizeBytes)
    {
        Category = category;
        Title = title;
        Summary = $"{count} 项 · {DiskCleanerFormat.Bytes(sizeBytes)}";
    }

    public string Category { get; }

    public string Title { get; }

    public string Summary { get; }
}
