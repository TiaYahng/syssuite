using System.Windows;

namespace SysSuite.UI;

public partial class AccountDialog : Window
{
    public string Email => EmailBox.Text.Trim();

    public AccountDialog(string email)
    {
        InitializeComponent();
        EmailBox.Text = email;
    }

    private void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            MessageBox.Show(this, "请输入有效邮箱。", "登录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        EmailBox.Text = string.Empty;
        DialogResult = true;
    }
}
