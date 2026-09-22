using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.UI.Controls;

public partial class LeftoverWindow : Window
{
    private readonly ILeftoverScanner scanner;

    public LeftoverWindow(IReadOnlyList<LeftoverItem> items)
    {
        InitializeComponent();
        scanner = ((App)Application.Current).Services.GetRequiredService<ILeftoverScanner>();
        var rows = items.Select(item => new LeftoverRow(item)).ToList();
        ItemsList.ItemsSource = rows;
        DeleteButton.IsEnabled = rows.Count > 0;
        StatusText.Text = rows.Count == 0 ? "检测到 0 个文件。" : $"检测到 {rows.Count} 项残留。";
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        var rows = ItemsList.ItemsSource.OfType<LeftoverRow>().Where(row => row.Selected).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "请先选择要删除的残留项。", "残留确认", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"删除 {rows.Count} 个选中残留项？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteButton.IsEnabled = false;
        var result = await scanner.DeleteAsync(rows.Select(row => row.Item with { Selected = true }).ToList());
        DeleteButton.IsEnabled = true;
        MessageBox.Show(this, result.IsSuccess ? $"已删除 {result.Value} 项。" : result.Message, "删除结果", MessageBoxButton.OK, result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (result.IsSuccess)
        {
            ItemsList.ItemsSource = Array.Empty<LeftoverRow>();
            DeleteButton.IsEnabled = false;
            StatusText.Text = $"已删除 {result.Value} 项残留。";
            DialogResult = true;
        }
    }

    private sealed class LeftoverRow
    {
        public LeftoverRow(LeftoverItem item)
        {
            Item = item;
            Selected = item.Selected;
        }

        public LeftoverItem Item { get; }

        public bool Selected { get; set; }

        public string Path => Item.Path;

        public string Reason => Item.Reason;

        public string KindLabel => Item.Kind switch
        {
            LeftoverKind.InstallDirectory => "安装目录",
            LeftoverKind.AppDataDirectory => "数据目录",
            LeftoverKind.RegistryKey => "注册表键",
            LeftoverKind.RegistryValue => "注册表值",
            _ => "未知"
        };
    }
}
