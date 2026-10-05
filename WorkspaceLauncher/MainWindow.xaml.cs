using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WorkspaceLauncher.Models;
using WorkspaceLauncher.Services;

namespace WorkspaceLauncher;

public partial class MainWindow : Window
{
    private readonly WorkspaceStore _store = new();
    private readonly WorkspaceLauncherService _launcher = new();
    private WorkspaceConfiguration _configuration = new();
    private ObservableCollection<WorkspaceProfile> _profiles = [];
    private WorkspaceProfile? _selectedWorkspace;
    private bool _isBusy;
    private bool _launchHadItemErrors;
    private int _desktopCount;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _desktopCount = new DesktopCountService().GetDesktopCount();
            for (int desktopIndex = 0; desktopIndex < _desktopCount; desktopIndex++)
            {
                DesktopComboBox.Items.Add($"Desktop {desktopIndex + 1}");
            }

            _configuration = _store.LoadOrCreate();
            _profiles = _configuration.Workspaces;
            foreach (WorkspaceProfile profile in _profiles)
            {
                if (profile.DesktopIndex >= _desktopCount)
                {
                    profile.DesktopIndex = _desktopCount - 1;
                    AppendLog($"{profile.Name} was assigned to Desktop {_desktopCount} because its previous desktop no longer exists.");
                }
            }

            WorkspaceList.ItemsSource = _profiles;
            if (_profiles.Count > 0)
            {
                WorkspaceList.SelectedIndex = 0;
            }

            _store.Save(_configuration);
            AppendLog($"Detected {_desktopCount} virtual desktop(s).");
            AppendLog($"Configuration: {_store.FilePath}");
            AppendLog("Select a workspace, edit its launch items, then save or launch.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Virtual desktops or workspace settings could not be initialized.\n\n{exception.Message}",
                "Workspace Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void WorkspaceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_selectedWorkspace is not null)
        {
            SaveProfiles(showSuccess: false);
        }

        _selectedWorkspace = WorkspaceList.SelectedItem as WorkspaceProfile;
        EditorPanel.DataContext = _selectedWorkspace;
        EditorPanel.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        DeleteWorkspaceButton.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        LaunchSequenceButton.IsEnabled = !_isBusy;
        NewWorkspaceButton.IsEnabled = !_isBusy;
        WorkspaceList.IsEnabled = !_isBusy;
        StatusText.Text = _selectedWorkspace is null ? "Create a workspace to get started." : $"Editing {_selectedWorkspace.Name}";
    }

    private void NewWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        int nameIndex = _profiles.Count + 1;
        string name;
        do
        {
            name = $"Workspace {nameIndex++}";
        } while (_profiles.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)));

        var profile = new WorkspaceProfile
        {
            Name = name,
            DesktopIndex = 0
        };
        _profiles.Add(profile);
        WorkspaceList.SelectedItem = profile;
        AppendLog($"Created {profile.Name}. Add launch items and save your changes.");
        SaveProfiles(showSuccess: false);
    }

    private void DeleteWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }

        if (_profiles.Count == 1)
        {
            MessageBox.Show("Keep at least one workspace. Create another before deleting this one.",
                "Cannot delete workspace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Delete the “{_selectedWorkspace.Name}” workspace?",
            "Delete workspace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        string deletedName = _selectedWorkspace.Name;
        int selectedIndex = WorkspaceList.SelectedIndex;
        _configuration.LaunchSequence.Remove(_selectedWorkspace.Id);
        _profiles.Remove(_selectedWorkspace);
        WorkspaceList.SelectedIndex = Math.Min(selectedIndex, _profiles.Count - 1);
        SaveProfiles(showSuccess: true);
        AppendLog($"Deleted {deletedName}.");
    }

    private void AddApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose an application or shortcut",
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string extension = Path.GetExtension(dialog.FileName);
        string appName = Path.GetFileNameWithoutExtension(dialog.FileName);
        var item = new LaunchItem
        {
            Kind = LaunchItemKind.Application,
            Name = appName,
            Target = dialog.FileName,
            ProcessName = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? appName : ""
        };
        _selectedWorkspace.Items.Add(item);
        LaunchItemsGrid.SelectedItem = item;
        AppendLog($"Added application: {item.Name}");
    }

    private void AddWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }

        string address = WebsiteUrlBox.Text.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("Enter a complete website address beginning with http:// or https://.",
                "Invalid website", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = new LaunchItem
        {
            Kind = LaunchItemKind.Website,
            Name = uri.Host,
            Target = uri.AbsoluteUri
        };
        _selectedWorkspace.Items.Add(item);
        LaunchItemsGrid.SelectedItem = item;
        WebsiteUrlBox.Clear();
        AppendLog($"Added website: {item.Target}");
    }

    private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null || LaunchItemsGrid.SelectedItem is not LaunchItem item)
        {
            return;
        }

        _selectedWorkspace.Items.Remove(item);
        AppendLog($"Removed launch item: {item.Name}");
    }

    private void MoveItemUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(-1);

    private void MoveItemDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(1);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveProfiles(showSuccess: true);
    }

    private void ConfigureSequenceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SequenceWindow(_profiles, _configuration.LaunchSequence)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Guid[] previousSequence = _configuration.LaunchSequence.ToArray();
        _configuration.LaunchSequence.Clear();
        foreach (Guid id in dialog.SequenceIds)
        {
            _configuration.LaunchSequence.Add(id);
        }

        if (!SaveProfiles(showSuccess: true))
        {
            _configuration.LaunchSequence.Clear();
            foreach (Guid id in previousSequence)
            {
                _configuration.LaunchSequence.Add(id);
            }
        }
        else
        {
            AppendLog($"Saved launch sequence with {_configuration.LaunchSequence.Count} workspace(s).");
        }
    }

    private async void LaunchSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null || !SaveProfiles(showSuccess: false))
        {
            return;
        }

        WorkspaceProfile profile = _selectedWorkspace;
        await RunLaunchOperationAsync(() => _launcher.LaunchProfileAsync(profile, ReportActivity));
    }

    private async void LaunchSequenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveProfiles(showSuccess: false))
        {
            return;
        }

        var profilesById = _profiles.ToDictionary(profile => profile.Id);
        WorkspaceProfile[] sequence = _configuration.LaunchSequence
            .Select(id => profilesById[id])
            .ToArray();
        await RunLaunchOperationAsync(() => _launcher.LaunchSequenceAsync(sequence, ReportActivity));
    }

    private void MoveSelectedItem(int offset)
    {
        if (_selectedWorkspace is null || LaunchItemsGrid.SelectedItem is not LaunchItem item)
        {
            return;
        }

        int oldIndex = _selectedWorkspace.Items.IndexOf(item);
        int newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= _selectedWorkspace.Items.Count)
        {
            return;
        }

        _selectedWorkspace.Items.Move(oldIndex, newIndex);
        LaunchItemsGrid.SelectedItem = item;
        LaunchItemsGrid.ScrollIntoView(item);
    }

    private bool SaveProfiles(bool showSuccess)
    {
        LaunchItemsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, exitEditingMode: true);
        LaunchItemsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, exitEditingMode: true);

        if (_profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
        {
            ShowValidationError("Every workspace needs a name.");
            return false;
        }

        if (_profiles.GroupBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            ShowValidationError("Workspace names must be unique.");
            return false;
        }

        if (_profiles.Any(profile => profile.DesktopIndex < 0 || profile.DesktopIndex >= _desktopCount))
        {
            ShowValidationError($"Choose a virtual desktop from 1 to {_desktopCount}.");
            return false;
        }

        foreach (var profile in _profiles)
        {
            foreach (var item in profile.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Target))
                {
                    ShowValidationError($"Every launch item in “{profile.Name}” needs a name and target.");
                    return false;
                }

                if (item.Kind == LaunchItemKind.Website &&
                    (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                {
                    ShowValidationError($"“{item.Name}” must use a valid http:// or https:// address.");
                    return false;
                }
            }
        }

        try
        {
            _store.Save(_configuration);
            if (showSuccess)
            {
                StatusText.Text = "Changes saved.";
                AppendLog("Workspace settings saved.");
            }

            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not save workspace settings.\n\n{exception.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Save failed.";
            return false;
        }
    }

    private void ShowValidationError(string message)
    {
        MessageBox.Show(message, "Check workspace settings", MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = "Please fix the workspace settings before saving or launching.";
    }

    private async Task RunLaunchOperationAsync(Func<Task> launch)
    {
        SetBusy(true);
        ActivityLog.Clear();
        _launchHadItemErrors = false;
        try
        {
            await launch();
            StatusText.Text = _launchHadItemErrors
                ? "Launch sequence finished with errors."
                : "Launch sequence finished.";
            AppendLog(_launchHadItemErrors
                ? "Launch sequence finished with errors. Review the items marked ERROR."
                : "Launch sequence finished.");
        }
        catch (Exception exception)
        {
            StatusText.Text = "Launch sequence stopped because of an error.";
            AppendLog($"ERROR: {exception.Message}");
            MessageBox.Show(
                $"The launch sequence could not continue.\n\n{exception.Message}",
                "Launch failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        EditorPanel.IsEnabled = !isBusy && _selectedWorkspace is not null;
        LaunchSequenceButton.IsEnabled = !isBusy;
        ConfigureSequenceButton.IsEnabled = !isBusy;
        NewWorkspaceButton.IsEnabled = !isBusy;
        WorkspaceList.IsEnabled = !isBusy;
        DeleteWorkspaceButton.IsEnabled = !isBusy && _selectedWorkspace is not null;
        SaveButton.IsEnabled = !isBusy;
    }

    private void ReportActivity(string message)
    {
        if (message.StartsWith("ERROR:", StringComparison.Ordinal))
        {
            _launchHadItemErrors = true;
        }

        StatusText.Text = message;
        AppendLog(message);
    }

    private void AppendLog(string message)
    {
        ActivityLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ActivityLog.ScrollToEnd();
    }
}
