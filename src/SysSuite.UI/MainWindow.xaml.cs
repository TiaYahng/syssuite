using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.UI.Services;

namespace SysSuite.UI;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed record NavItem(NavPage Page, string Icon, string Title);

    private static readonly NavItem SettingsNavItem = new(NavPage.Settings, "⚙️", "设置");

    private static readonly NavItem[] NavItems =
    [
        new(NavPage.Dashboard, "🏠", "仪表盘"),
        new(NavPage.SystemInfo, "💻", "系统信息"),
        new(NavPage.Cleaner, "🧹", "磁盘清理"),
        new(NavPage.Uninstaller, "📦", "应用管理"),
        new(NavPage.Security, "🛡️", "安全中心"),
        new(NavPage.Desktop, "🖼️", "桌面整理"),
        new(NavPage.SoftwareHub, "🔄", "软件管家"),
        new(NavPage.Toolbox, "🧰", "工具箱")
    ];

    private readonly INavigationService navigationService;
    private readonly ISettingsService settingsService;
    private UiLayout currentLayout = UiLayout.Sidebar;

    public MainWindow()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        navigationService = services.GetRequiredService<INavigationService>();
        settingsService = services.GetRequiredService<ISettingsService>();
        ApplyLayout(Enum.TryParse(settingsService.Current.UiLayout, out UiLayout layout) ? layout : UiLayout.Sidebar);
        var savedTheme = Enum.TryParse(settingsService.Current.Theme, out AppTheme theme) ? theme : AppTheme.System;
        ApplyTheme(savedTheme, false);
        NavigateTo(NavItems[0]);
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnStateChanged;
    }

    private void OnThemeClick(object sender, RoutedEventArgs args)
    {
        var nextTheme = Enum.TryParse(settingsService.Current.Theme, out AppTheme savedTheme) ? savedTheme : AppTheme.System;
        nextTheme = nextTheme switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System
        };
        ApplyTheme(nextTheme, true);
    }

    private void ApplyTheme(AppTheme theme, bool persist)
    {
        ThemeService.Apply(theme);
        UpdateNavigationSelection(navigationService.CurrentPage);
        if (persist)
        {
            settingsService.Current.Theme = theme.ToString();
            settingsService.SaveDebounced();
        }
    }

    private void OnAccountClick(object sender, RoutedEventArgs args)
    {
        var dialog = new AccountDialog(settingsService.Current.AccountEmail) { Owner = this };
        dialog.ShowDialog();
        if (dialog.DialogResult == true)
        {
            settingsService.Current.AccountEmail = dialog.Email;
            settingsService.SaveDebounced();
        }
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        NavigateTo(SettingsNavItem);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            SearchBox.Text = string.Empty;
            args.Handled = true;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs args) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs args) => Close();
}
