using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class LaunchItemDialog : Window
{
    public LaunchItem? Item { get; private set; }

    public LaunchItemDialog(LaunchItem? existingItem = null)
    {
        InitializeComponent();
        if (existingItem is not null)
        {
            Title = "Edit launch item";
            DialogHeading.Text = "Edit launch item";
            SaveItemButton.Content = "Save launch item";
            SaveItemButton.ToolTip = "Validate and save these launch item settings.";
            ItemNameBox.Text = existingItem.Name;
            ItemTypeComboBox.SelectedItem = ItemTypeComboBox.Items
                .OfType<ComboBoxItem>()
                .First(comboBoxItem => Equals(comboBoxItem.Tag?.ToString(), existingItem.Kind.ToString()));
            if (existingItem.Kind == LaunchItemKind.Website)
            {
                WebsiteAddressBox.Text = existingItem.Target;
            }
            else if (existingItem.Kind == LaunchItemKind.File)
            {
                FilePathBox.Text = existingItem.Target;
                ProcessNameBox.Text = existingItem.ProcessName;
                WindowTitleBox.Text = existingItem.WindowTitle;
            }
            else
            {
                ApplicationPathBox.Text = existingItem.Target;
                ProcessNameBox.Text = existingItem.ProcessName;
                WindowTitleBox.Text = existingItem.WindowTitle;
            }
        }

        UpdateTypeFields();
        Loaded += (_, _) => GetActiveTargetBox().Focus();
    }

    private bool IsWebsiteSelected =>
        ItemTypeComboBox.SelectedItem is ComboBoxItem { Tag: "Website" };

    private bool IsFileSelected =>
        ItemTypeComboBox.SelectedItem is ComboBoxItem { Tag: "File" };

    private void ItemTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTypeFields();
    }

    private void UpdateTypeFields()
    {
        if (ApplicationFieldsPanel is null)
        {
            return;
        }

        bool isWebsite = IsWebsiteSelected;
        bool isFile = IsFileSelected;
        ApplicationFieldsPanel.Visibility = isWebsite || isFile ? Visibility.Collapsed : Visibility.Visible;
        WebsiteFieldsPanel.Visibility = isWebsite ? Visibility.Visible : Visibility.Collapsed;
        FileFieldsPanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        ApplicationOptionsPanel.Visibility = isWebsite ? Visibility.Collapsed : Visibility.Visible;
        if (SaveItemButton is not null)
        {
            SaveItemButton.ToolTip = isWebsite
                ? "Validate and add this website to the workspace."
                : isFile
                    ? "Validate and add this file to the workspace."
                    : "Validate and add this application to the workspace.";
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        bool isFile = IsFileSelected;
        var dialog = new OpenFileDialog
        {
            Title = isFile ? "Choose a file" : "Choose an application or shortcut",
            Filter = isFile
                ? "All files (*.*)|*.*"
                : "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TextBox targetBox = GetActiveTargetBox();
        string previousPath = targetBox.Text;
        targetBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(ItemNameBox.Text) ||
            string.Equals(
                Path.GetFileNameWithoutExtension(previousPath),
                ItemNameBox.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            ItemNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        if (!isFile &&
            Path.GetExtension(dialog.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(ProcessNameBox.Text) ||
             string.Equals(
                 Path.GetFileNameWithoutExtension(previousPath),
                 ProcessNameBox.Text.Trim(),
                 StringComparison.OrdinalIgnoreCase)))
        {
            ProcessNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void SaveItemButton_Click(object sender, RoutedEventArgs e)
    {
        string name = ItemNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationMessage.Text = "Enter a name for this launch item.";
            ItemNameBox.Focus();
            return;
        }

        if (IsWebsiteSelected)
        {
            string address = WebsiteAddressBox.Text.Trim();
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                ValidationMessage.Text = "Enter a complete website address beginning with http:// or https://.";
                WebsiteAddressBox.Focus();
                return;
            }

            Item = new LaunchItem
            {
                Kind = LaunchItemKind.Website,
                Name = name,
                Target = uri.AbsoluteUri
            };
            DialogResult = true;
            return;
        }

        if (IsFileSelected)
        {
            string filePath = FilePathBox.Text.Trim();
            if (!File.Exists(filePath))
            {
                ValidationMessage.Text = "Choose an existing file to open with its Windows default app.";
                FilePathBox.Focus();
                return;
            }

            Item = new LaunchItem
            {
                Kind = LaunchItemKind.File,
                Name = name,
                Target = filePath,
                ProcessName = ProcessNameBox.Text.Trim(),
                WindowTitle = WindowTitleBox.Text.Trim()
            };
            DialogResult = true;
            return;
        }

        string target = ApplicationPathBox.Text.Trim();
        string processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            ValidationMessage.Text = "Choose or enter an application executable or shortcut.";
            ApplicationPathBox.Focus();
            return;
        }

        if (LooksLikeFilePath(target) && !File.Exists(target))
        {
            ValidationMessage.Text = "The application path does not exist. Browse to a valid .exe or .lnk file.";
            ApplicationPathBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(processName) &&
            Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = Path.GetFileNameWithoutExtension(target);
        }

        Item = new LaunchItem
        {
            Kind = LaunchItemKind.Application,
            Name = name,
            Target = target,
            ProcessName = processName,
            WindowTitle = WindowTitleBox.Text.Trim()
        };
        DialogResult = true;
    }

    private static bool LooksLikeFilePath(string target)
    {
        return Path.IsPathRooted(target) ||
               target.Contains(Path.DirectorySeparatorChar) ||
               target.Contains(Path.AltDirectorySeparatorChar);
    }

    private TextBox GetActiveTargetBox() =>
        IsWebsiteSelected ? WebsiteAddressBox : IsFileSelected ? FilePathBox : ApplicationPathBox;
}
