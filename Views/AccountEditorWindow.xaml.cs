using System.Windows;
using AiWebGateway.Models;

namespace AiWebGateway.Views;

public partial class AccountEditorWindow : Window
{
    public Account Result { get; private set; }

    public AccountEditorWindow(Account? account = null)
    {
        InitializeComponent();
        Result = account ?? new Account();
        IdBox.Text = Result.Id;
        TokenBox.Text = Result.Token;
        CookieBox.Text = Result.Cookie;
        IdBox.Focus();
        IdBox.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new Account
        {
            Id = IdBox.Text.Trim(),
            Token = TokenBox.Text.Trim(),
            Cookie = CookieBox.Text.Trim(),
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}