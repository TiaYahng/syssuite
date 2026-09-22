using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SysSuite.UI.Services;

namespace SysSuite.UI;

public partial class MainWindow : Window
{
    private enum NavDisplay
    {
        Full,
        Icon,
        Ribbon
    }

    private void BuildNavigation(UiLayout layout)
    {
        SidebarPanel.Children.Clear();
        RibbonPanel.Children.Clear();

        foreach (var item in NavItems)
        {
            if (layout == UiLayout.Ribbon)
            {
                RibbonPanel.Children.Add(CreateNavButton(item, NavDisplay.Ribbon));
            }
            else
            {
                var display = layout is UiLayout.SidebarIcons or UiLayout.RightSidebarIcons ? NavDisplay.Icon : NavDisplay.Full;
                SidebarPanel.Children.Add(CreateNavButton(item, display));
            }
        }
    }

    private Button CreateNavButton(NavItem item, NavDisplay display)
    {
        var icon = new TextBlock
        {
            Text = item.Icon,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
            FontSize = display == NavDisplay.Icon ? 14 : 12,
            FontWeight = FontWeights.ExtraLight,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        icon.Margin = display switch
        {
            NavDisplay.Ribbon => new Thickness(0, 0, 0, -4),
            NavDisplay.Full => new Thickness(0, 0, 0, 0),
            _ => new Thickness(0)
        };
        object content;
        if (display == NavDisplay.Ribbon)
        {
            content = new TextBlock
            {
                Text = item.Title,
                FontSize = 14,
                TextAlignment = TextAlignment.Center
            };
        }
        else if (display == NavDisplay.Icon)
        {
            content = icon;
        }
        else
        {
            var title = new TextBlock { Text = item.Title, FontSize = display == NavDisplay.Ribbon ? 12 : 14 };
            var panel = display == NavDisplay.Ribbon
                ? new StackPanel { Children = { icon, title } }
                : new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } };
            panel.Margin = display == NavDisplay.Ribbon ? new Thickness(0) : new Thickness(0, 0, 3, 0);

            if (display == NavDisplay.Full)
            {
                panel.Margin = new Thickness(0, 0, 3, 0);
            }

            content = panel;
        }

        var button = new Button
        {
            Tag = item,
            Content = content,
            Width = display switch
            {
                NavDisplay.Ribbon => double.NaN,
                NavDisplay.Icon => 36,
                _ => 108
            },
            Height = display switch
            {
                NavDisplay.Ribbon => 32,
                _ => 36
            },
            Margin = display switch
            {
                NavDisplay.Icon => new Thickness(0, 0, 0, 4),
                NavDisplay.Ribbon => new Thickness(0, 0, 0, 0),
                _ => new Thickness(0, 0, 4, 4)
            },
            HorizontalContentAlignment = display == NavDisplay.Full ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = display switch
            {
                NavDisplay.Icon => new Thickness(0),
                NavDisplay.Ribbon => new Thickness(8, 3, 8, 3),
                _ => new Thickness(8)
            },
            ToolTip = item.Title
        };
        button.Click += OnNavClick;
        return button;
    }

    private void OnNavClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: NavItem item })
        {
            NavigateTo(item);
        }
    }

    private void NavigateTo(NavItem item)
    {
        PageHost.Content = navigationService.GetPage(item.Page);
        UpdateNavigationSelection(item.Page);
    }

    private void UpdateNavigationSelection(NavPage currentPage)
    {
        foreach (var button in RibbonPanel.Children.OfType<Button>().Concat(SidebarPanel.Children.OfType<Button>()))
        {
            var isSelected = button.Tag is NavItem item && item.Page == currentPage;
            button.SetResourceReference(Button.BackgroundProperty, isSelected ? "App.Accent" : "App.Transparent");
            button.SetResourceReference(Button.BorderBrushProperty, isSelected ? "App.Accent" : "App.Transparent");
            button.SetResourceReference(Button.ForegroundProperty, isSelected ? "App.AccentText" : "App.Text");
            button.BorderThickness = new Thickness(isSelected ? 1 : 0);
        }
    }
    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var cornerPreference = 2;
        _ = DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private void OnLayoutButtonClick(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu();
        var modes = new[] { (UiLayout.Ribbon, "水平"), (UiLayout.Sidebar, "左侧"), (UiLayout.RightSidebar, "右侧") };
        foreach (var (layout, title) in modes)
        {
            var menuItem = new MenuItem
            {
                Header = title,
                Tag = layout,
                IsCheckable = true,
                IsChecked = layout == currentLayout
            };
            menuItem.Click += OnLayoutModeClick;
            menu.Items.Add(menuItem);
        }

        menu.PlacementTarget = LayoutButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnLayoutModeClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: UiLayout layout } && layout != currentLayout)
        {
            ApplyLayout(layout);
            settingsService.Current.UiLayout = layout.ToString();
            settingsService.SaveDebounced();
        }
    }

    private void OnSidebarPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ClickCount != 2 || IsWithinButton(args.OriginalSource as DependencyObject))
        {
            return;
        }

        var nextLayout = currentLayout switch
        {
            UiLayout.Sidebar => UiLayout.SidebarIcons,
            UiLayout.SidebarIcons => UiLayout.Sidebar,
            UiLayout.RightSidebar => UiLayout.RightSidebarIcons,
            UiLayout.RightSidebarIcons => UiLayout.RightSidebar,
            _ => currentLayout
        };
        ApplyLayout(nextLayout);
        settingsService.Current.UiLayout = nextLayout.ToString();
        settingsService.SaveDebounced();
        args.Handled = true;
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ApplyLayout(UiLayout layout)
    {
        currentLayout = layout;
        var isRight = layout is UiLayout.RightSidebar or UiLayout.RightSidebarIcons;
        var isIcon = layout is UiLayout.SidebarIcons or UiLayout.RightSidebarIcons;
        var sidebarWidth = isIcon ? new GridLength(46) : new GridLength(118);
        LeftSidebarColumn.Width = layout == UiLayout.Ribbon || isRight ? new GridLength(0) : sidebarWidth;
        RightSidebarColumn.Width = isRight ? sidebarWidth : new GridLength(0);
        Grid.SetColumn(SidebarHost, isRight ? 2 : 0);
        SidebarHost.BorderThickness = isRight ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        RibbonContainer.Visibility = layout == UiLayout.Ribbon ? Visibility.Visible : Visibility.Collapsed;
        SidebarHost.Visibility = layout == UiLayout.Ribbon ? Visibility.Collapsed : Visibility.Visible;
        PageHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        PageHost.VerticalAlignment = VerticalAlignment.Stretch;
        PageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        PageHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        PageHost.MaxWidth = double.PositiveInfinity;
        SidebarPanel.Margin = isIcon ? new Thickness(4) : new Thickness(8);
        LayoutButton.Content = CreateLayoutIcon(layout);
        BuildNavigation(layout);
        UpdateNavigationSelection(navigationService.CurrentPage);
    }

    private static System.Windows.Controls.Grid CreateLayoutIcon(UiLayout layout)
    {
        var grid = new System.Windows.Controls.Grid { Width = 14, Height = 14 };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(2) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(2) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (var row = 0; row < 3; row += 2)
        {
            for (var column = 0; column < 3; column += 2)
            {
                var tile = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(2) };
                if (row == 2 && column == 2)
                {
                    tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "App.Accent");
                    Grid.SetRow(tile, row);
                    Grid.SetColumn(tile, column);
                    grid.Children.Add(tile);
                    continue;
                }

                tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "App.Text");
                Grid.SetRow(tile, row);
                Grid.SetColumn(tile, column);
                grid.Children.Add(tile);
            }
        }

        return grid;
    }

}
