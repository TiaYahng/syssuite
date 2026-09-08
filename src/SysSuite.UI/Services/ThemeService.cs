using System.Windows;

namespace SysSuite.UI.Services;

public sealed class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (dictionaries[index].Source?.OriginalString.Contains("/Themes/") == true)
            {
                dictionaries.RemoveAt(index);
            }
        }

        var selectedTheme = theme == AppTheme.System ? GetSystemTheme() : theme;
        var themeFile = selectedTheme == AppTheme.Light ? "Light.xaml" : "Dark.xaml";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/SysSuite.UI;component/Themes/{themeFile}", UriKind.Relative)
        });
    }

    private static AppTheme GetSystemTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var value = key?.GetValue("AppsUseLightTheme") as int?;
        return value == 0 ? AppTheme.Dark : AppTheme.Light;
    }
}
