using System.Collections.ObjectModel;
using System.Windows;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class CloseWorkspaceWindow : Window
{
    private readonly ObservableCollection<CloseWorkspaceItem> _items;

    public IReadOnlyList<LaunchItem> SelectedItems =>
        _items.Where(item => item.IsChecked).Select(item => item.Item).ToArray();

    public CloseWorkspaceWindow(string workspaceName, IEnumerable<LaunchItem> applicationItems)
    {
        InitializeComponent();
        WorkspaceDescription.Text = $"Select applications and files from “{workspaceName}” to close.";
        _items = new ObservableCollection<CloseWorkspaceItem>(
            applicationItems
                .Where(item => item.Kind is LaunchItemKind.Application or LaunchItemKind.File)
                .Select(item => new CloseWorkspaceItem(item)));
        ApplicationItems.ItemsSource = _items;
        NoApplicationsHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count == 0)
        {
            NoApplicationsHint.Text = "This workspace has no configured application or file entries. Website entries cannot be closed from Workspace Manager.";
        }
        UpdateCloseButton();
    }

    private void SelectionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox &&
            checkBox.DataContext is CloseWorkspaceItem item)
        {
            item.IsChecked = checkBox.IsChecked == true;
        }
        UpdateCloseButton();
    }

    private void CloseSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void UpdateCloseButton()
    {
        CloseSelectedButton.IsEnabled = _items.Any(item => item.IsChecked);
    }

    public sealed class CloseWorkspaceItem
    {
        public LaunchItem Item { get; }
        public bool IsChecked { get; set; } = true;

        public CloseWorkspaceItem(LaunchItem item)
        {
            Item = item;
        }
    }
}
