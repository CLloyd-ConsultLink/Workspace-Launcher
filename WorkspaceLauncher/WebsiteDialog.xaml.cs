using System.Windows;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class WebsiteDialog : Window
{
    public LaunchItem? Website { get; private set; }

    public WebsiteDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => WebsiteNameBox.Focus();
    }

    private void AddWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        string name = WebsiteNameBox.Text.Trim();
        string address = WebsiteUrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationMessage.Text = "Enter a name for the website.";
            WebsiteNameBox.Focus();
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            ValidationMessage.Text = "Enter a complete website address beginning with http:// or https://.";
            WebsiteUrlBox.Focus();
            return;
        }

        Website = new LaunchItem
        {
            Kind = LaunchItemKind.Website,
            Name = name,
            Target = uri.AbsoluteUri
        };
        DialogResult = true;
    }
}
