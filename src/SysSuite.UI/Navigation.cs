using System.Windows.Controls;
using System.Windows;
using SysSuite.UI.Pages;

namespace SysSuite.UI;

public interface INavigationService
{
    event EventHandler<NavPage>? CurrentPageChanged;

    NavPage CurrentPage { get; }

    UserControl? GetPage(NavPage page);
}

public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<NavPage, Func<UserControl>> pageFactories = new()
    {
        [NavPage.Dashboard] = () => new DashboardPage(),
        [NavPage.SystemInfo] = () => new SystemInfoPage(),
        [NavPage.Cleaner] = () => new CleanerPage(),
        [NavPage.Uninstaller] = () => new UninstallerPage(),
        [NavPage.Security] = () => new SecurityPage(),
        [NavPage.Desktop] = () => new DesktopPage(),
        [NavPage.SoftwareHub] = () => new SoftwareHubPage(),
        [NavPage.Toolbox] = () => new ToolboxPage(),
        [NavPage.Settings] = () => new SettingsPage()
    };

    private readonly Dictionary<NavPage, UserControl> cachedPages = new();

    public event EventHandler<NavPage>? CurrentPageChanged;

    public NavPage CurrentPage { get; private set; }

    public UserControl? GetPage(NavPage page)
    {
        CurrentPage = page;
        CurrentPageChanged?.Invoke(this, page);
        if (cachedPages.TryGetValue(page, out var cachedPage))
        {
            return cachedPage;
        }

        if (!pageFactories.TryGetValue(page, out var factory))
        {
            return null;
        }

        var instance = factory();
        instance.HorizontalAlignment = HorizontalAlignment.Stretch;
        instance.VerticalAlignment = VerticalAlignment.Stretch;
        instance.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        instance.VerticalContentAlignment = VerticalAlignment.Stretch;
        cachedPages[page] = instance;
        return instance;
    }
}
