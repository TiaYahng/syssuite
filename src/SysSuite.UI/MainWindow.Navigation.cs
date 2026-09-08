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
            FontSize = display == NavDisplay.Ribbon ? 18 : display == NavDisplay.Icon ? 18 : 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        object content;
        if (display == NavDisplay.Icon)
        {
            content = icon;
        }
        else
        {
            var title = new TextBlock { Text = item.Title, FontSize = display == NavDisplay.Ribbon ? 12 : 14 };
            var panel = display == NavDisplay.Ribbon
                ? new StackPanel { Children = { icon, title } }
                : new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } };

            if (display == NavDisplay.Full)
            {
                panel.Margin = new Thickness(0, 0, 6, 0);
            }

            content = panel;
        }

        var button = new Button
        {
            Tag = item,
            Content = content,
            Width = display switch
            {
                NavDisplay.Ribbon => 84,
                NavDisplay.Icon => 36,
                _ => 108
            },
            Height = display switch
            {
                NavDisplay.Ribbon => 56,
                _ => 36
            },
            Margin = display == NavDisplay.Icon ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 4, 4),
            HorizontalContentAlignment = display == NavDisplay.Full ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = display == NavDisplay.Icon ? new Thickness(0) : new Thickness(8),
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
        StatusText.Text = item.Title;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var cornerPreference = 2;
        _ = DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));
    }

    private void BindStatusBar()
    {
        var taskManager = BackgroundTaskManager.Current;
        TaskProgress.DataContext = taskManager;
        TaskProgress.SetBinding(RangeBase.ValueProperty, new Binding(nameof(BackgroundTaskManager.ProgressPercent))
        {
            Mode = BindingMode.OneWay
        });
        TaskProgress.SetBinding(VisibilityProperty, new Binding(nameof(BackgroundTaskManager.CancelCommand))
        {
            Converter = new BooleanToVisibilityConverter()
        });
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton == MouseButton.Left && args.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnToggleLayout(object sender, RoutedEventArgs args)
    {
        var layout = currentLayout switch
        {
            UiLayout.Ribbon => UiLayout.RightSidebar,
            UiLayout.RightSidebar => UiLayout.Sidebar,
            _ => UiLayout.Ribbon
        };
        ApplyLayout(layout);
        settingsService.Current.UiLayout = layout.ToString();
        settingsService.SaveDebounced();
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
        LeftSidebarColumn.Width = !isRight ? sidebarWidth : new GridLength(0);
        RightSidebarColumn.Width = isRight ? sidebarWidth : new GridLength(0);
        Grid.SetColumn(SidebarHost, isRight ? 2 : 0);
        SidebarHost.BorderThickness = isRight ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        RibbonContainer.Visibility = layout == UiLayout.Ribbon ? Visibility.Visible : Visibility.Collapsed;
        SidebarHost.Visibility = layout == UiLayout.Ribbon ? Visibility.Collapsed : Visibility.Visible;
        SidebarPanel.Margin = isIcon ? new Thickness(4) : new Thickness(8);
        var arrow = new TextBlock
        {
            Text = layout is UiLayout.Sidebar or UiLayout.SidebarIcons ? "←" :
                layout is UiLayout.RightSidebar or UiLayout.RightSidebarIcons ? "→" : "↑",
            FontSize = 16
        };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "App.Text");
        LayoutButton.Content = arrow;
        BuildNavigation(layout);
    }

}
