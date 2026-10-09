using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WorkspaceLauncher.Models;
using WorkspaceLauncher.Services;

namespace WorkspaceLauncher;

public partial class MainWindow : Window
{
    private readonly WorkspaceStore _store = new();
    private readonly WorkspaceLauncherService _launcher = new();
    private readonly UpdateService _updateService = new();
    private WorkspaceConfiguration _configuration = new();
    private ObservableCollection<WorkspaceProfile> _profiles = [];
    private WorkspaceProfile? _selectedWorkspace;
    private bool _isBusy;
    private bool _launchHadItemErrors;
    private bool _updateCheckStarted;
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
            NoWorkspacesHint.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_profiles.Count > 0)
            {
                WorkspaceList.SelectedIndex = 0;
            }
            UpdateEmptyState();

            _store.Save(_configuration);
            AppendLog($"Detected {_desktopCount} virtual desktop(s).");
            AppendLog($"Configuration: {_store.FilePath}");
            AppendLog(_profiles.Count == 0
                ? "Choose + New to create your first workspace."
                : "Select a workspace, edit its launch items, then save or launch.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Virtual desktops or workspace settings could not be initialized.\n\n{exception.Message}",
                "Workspace Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_updateCheckStarted)
        {
            return;
        }

        _updateCheckStarted = true;

        UpdateRelease? update;
        try
        {
            update = await _updateService.CheckForUpdateAsync();
        }
        catch (Exception exception)
        {
            AppendLog($"Could not check for updates; you can continue using the app. {exception.Message}");
            StatusText.Text = "Update check failed; Workspace Manager is ready.";
            return;
        }

        if (update is null)
        {
            return;
        }

        while (_isBusy)
        {
            await Task.Delay(250);
        }

        AppendLog($"Workspace Manager {update.TagName} is available.");
        MessageBoxResult choice = MessageBox.Show(
            $"Workspace Manager {update.Version} is available. Download and start the installer now?\n\n" +
            "Workspace Manager will close while the update is installed.",
            "Update available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (choice != MessageBoxResult.Yes)
        {
            AppendLog("Update postponed. You will be asked again the next time the app starts.");
            return;
        }

        SetBusy(true);
        try
        {
            StatusText.Text = "Downloading and verifying the update...";
            AppendLog("Downloading the update installer...");
            string installerPath = await _updateService.DownloadInstallerAsync(update);

            using Process? installer = Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true
            });
            if (installer is null)
            {
                throw new InvalidOperationException("Windows did not start the update installer.");
            }

            AppendLog("Verified installer started. Closing Workspace Manager for the update.");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            SetBusy(false);
            AppendLog($"ERROR: The update could not be installed. {exception.Message}");
            StatusText.Text = "Update failed; Workspace Manager is ready.";
            MessageBox.Show(
                $"The update could not be downloaded or started.\n\n{exception.Message}\n\n" +
                "You can continue using Workspace Manager and try again next time.",
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void WorkspaceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_selectedWorkspace is not null)
        {
            _selectedWorkspace.Items.CollectionChanged -= SelectedWorkspaceItems_CollectionChanged;
            SaveProfiles(showSuccess: false);
        }

        _selectedWorkspace = WorkspaceList.SelectedItem as WorkspaceProfile;
        if (_selectedWorkspace is not null)
        {
            _selectedWorkspace.Items.CollectionChanged += SelectedWorkspaceItems_CollectionChanged;
        }

        EditorPanel.DataContext = _selectedWorkspace;
        EditorPanel.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        DeleteWorkspaceButton.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        LaunchSequenceButton.IsEnabled = !_isBusy;
        NewWorkspaceButton.IsEnabled = !_isBusy;
        WorkspaceList.IsEnabled = !_isBusy;
        NoWorkspacesHint.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _selectedWorkspace is null ? "Create a workspace to get started." : $"Editing {_selectedWorkspace.Name}";
        UpdateEmptyState();
    }

    private void DesktopComboBox_DropDownOpened(object sender, EventArgs e)
    {
        try
        {
            int detectedCount = new DesktopCountService().GetDesktopCount();
            if (detectedCount == _desktopCount)
            {
                return;
            }

            if (detectedCount < _desktopCount)
            {
                foreach (WorkspaceProfile profile in _profiles)
                {
                    if (profile.DesktopIndex >= detectedCount)
                    {
                        profile.DesktopIndex = detectedCount - 1;
                        AppendLog($"{profile.Name} was reassigned to Desktop {detectedCount} because its previous desktop no longer exists.");
                    }
                }
            }

            while (DesktopComboBox.Items.Count > detectedCount)
            {
                DesktopComboBox.Items.RemoveAt(DesktopComboBox.Items.Count - 1);
            }

            while (DesktopComboBox.Items.Count < detectedCount)
            {
                DesktopComboBox.Items.Add($"Desktop {DesktopComboBox.Items.Count + 1}");
            }

            _desktopCount = detectedCount;
            _store.Save(_configuration);
            AppendLog($"Detected {_desktopCount} virtual desktop(s); desktop choices refreshed.");
            StatusText.Text = $"Desktop choices refreshed: {_desktopCount} available.";
        }
        catch (Exception exception)
        {
            ReportActivity($"ERROR: Could not refresh virtual desktop choices. {exception.Message}");
            MessageBox.Show(
                $"Could not refresh the available virtual desktops.\n\n{exception.Message}",
                "Desktop detection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

    private void AddLaunchItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }

        var dialog = new LaunchItemDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Item is null)
        {
            return;
        }

        _selectedWorkspace.Items.Add(dialog.Item);
        LaunchItemsGrid.SelectedItem = dialog.Item;
        AppendLog($"Added {GetLaunchItemTypeLabel(dialog.Item.Kind)}: {dialog.Item.Name}");
    }

    private void LaunchItemsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject originalSource ||
            ItemsControl.ContainerFromElement(LaunchItemsGrid, originalSource) is not DataGridRow ||
            LaunchItemsGrid.SelectedItem is not LaunchItem item)
        {
            return;
        }

        var dialog = new LaunchItemDialog(item) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Item is null)
        {
            return;
        }

        item.Kind = dialog.Item.Kind;
        item.Name = dialog.Item.Name;
        item.Target = dialog.Item.Target;
        item.ProcessName = dialog.Item.ProcessName;
        item.WindowTitle = dialog.Item.WindowTitle;
        AppendLog($"Updated {GetLaunchItemTypeLabel(item.Kind)}: {item.Name}");
    }

    private static string GetLaunchItemTypeLabel(LaunchItemKind kind) =>
        kind == LaunchItemKind.Website ? "website" : "application";

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
        var dialog = new SequenceWindow(
            _profiles,
            _configuration.LaunchSequence,
            _desktopCount,
            _configuration.EndDesktopIndex)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Guid[] previousSequence = _configuration.LaunchSequence.ToArray();
        int? previousEndDesktopIndex = _configuration.EndDesktopIndex;
        _configuration.LaunchSequence.Clear();
        foreach (Guid id in dialog.SequenceIds)
        {
            _configuration.LaunchSequence.Add(id);
        }
        _configuration.EndDesktopIndex = dialog.EndDesktopIndex;

        if (!SaveProfiles(showSuccess: true))
        {
            _configuration.LaunchSequence.Clear();
            foreach (Guid id in previousSequence)
            {
                _configuration.LaunchSequence.Add(id);
            }
            _configuration.EndDesktopIndex = previousEndDesktopIndex;
        }
        else
        {
            string endDesktop = _configuration.EndDesktopIndex is int desktopIndex
                ? $"Desktop {desktopIndex + 1}"
                : "the last workspace's desktop";
            AppendLog($"Saved launch sequence with {_configuration.LaunchSequence.Count} workspace(s); ending on {endDesktop}.");
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
        await RunLaunchOperationAsync(() => _launcher.LaunchSequenceAsync(
            sequence,
            ReportActivity,
            _configuration.EndDesktopIndex));
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

    private void SelectedWorkspaceItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyItemsHint.Visibility = _selectedWorkspace is not null && _selectedWorkspace.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
