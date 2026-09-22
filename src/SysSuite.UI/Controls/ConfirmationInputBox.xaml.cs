using System.Windows;

namespace SysSuite.UI.Controls;

public partial class ConfirmationInputBox : Window
{
    private string requiredText = string.Empty;

    public ConfirmationInputBox()
    {
        InitializeComponent();
    }

    public static bool Show(Window owner, string title, string message, string requiredText)
    {
        var dialog = new ConfirmationInputBox
        {
            Owner = owner,
            Title = title
        };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.RequirementText.Text = $"请输入以下确认句后继续：{requiredText}";
        dialog.requiredText = requiredText;
        return dialog.ShowDialog() == true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs args)
    {
        if (!string.Equals(InputBox.Text, requiredText, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "确认句不匹配。", "无法执行", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
