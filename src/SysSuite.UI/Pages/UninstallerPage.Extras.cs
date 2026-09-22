using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace SysSuite.UI.Pages;

public partial class UninstallerPage
{
    private void OnOpenOnlineSearchClick(object sender, RoutedEventArgs args)
    {
        if (GetSelectedApp() is not { } app)
        {
            ShowWarning("请先选择一个应用。");
            return;
        }

        var parts = new[] { app.Name, app.Publisher, app.Version }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var query = string.Join(' ', parts);
        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
            UseShellExecute = true
        });
        StatusText.Text = "已在浏览器打开在线搜索。";
    }

    private void OnExportHtmlClick(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML 报告|*.html",
            FileName = "SysSuite-卸载器报告.html"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var rows = AppsList.Items.OfType<AppRow>().ToList();
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>SysSuite 卸载器报告</title>");
        builder.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;margin:24px;color:#111}");
        builder.AppendLine("table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;font-size:13px}th{background:#f3f3f3}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<h2>已安装程序报告</h2>"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<p>生成时间：{DateTime.Now:yyyy-MM-dd HH:mm} · 共 {rows.Count} 个程序</p>"));
        builder.AppendLine("<table><tr><th>程序</th><th>发布者</th><th>安装日期</th><th>大小</th><th>版本</th><th>来源</th></tr>");
        foreach (var row in rows)
        {
            builder.AppendLine("<tr>"
                + $"<td>{WebUtility.HtmlEncode(row.Name)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.Publisher)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.InstalledOn)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.SizeText)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.Version)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.SourceLabel)}</td>"
                + "</tr>");
        }

        builder.AppendLine("</table></body></html>");
        File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(false));
        StatusText.Text = $"报告已导出：{dialog.FileName}";
    }
}
