using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    private bool _closeHadWarnings;
    private bool _updateCheckStarted;
    private bool _updatingSequenceSelector;
    private int _desktopCount;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _ready;
    private bool _dirty;
    private bool _restoringWorkspaceSelection;

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += SaveTimer_Tick;

        try
        {
            _desktopCount = new DesktopCountService().GetDesktopCount();
            for (int desktopIndex = 0; desktopIndex < _desktopCount; desktopIndex++)
            {
                DesktopComboBox.Items.Add($"Desktop {desktopIndex + 1}");
            }

            _configuration = _store.LoadOrCreate();
            if (_configuration.DefaultSequenceId is Guid defaultId)
            {
                _configuration.SelectedSequenceId = defaultId;
            }
            _profiles = _configuration.Workspaces;
            RefreshSequenceSelector();
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
            _ready = true;
            AppendLog($"Detected {_desktopCount} virtual desktop(s).");
            AppendLog($"Configuration: {_store.FilePath}");
            AppendLog(_profiles.Count == 0
                ? "Choose + New to create your first workspace."
                : "Select a workspace to edit or launch it. Changes save automatically.");
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
            if (_dirty && !SaveProfiles(showSuccess: false))
            {
                SetBusy(false);
                return;
            }
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
        if (_restoringWorkspaceSelection)
        {
            return;
        }
        if (_ready && _dirty && !SaveProfiles(showSuccess: false))
        {
            _restoringWorkspaceSelection = true;
            WorkspaceList.SelectedItem = _selectedWorkspace;
            _restoringWorkspaceSelection = false;
            return;
        }
        if (_selectedWorkspace is not null)
        {
            _selectedWorkspace.Items.CollectionChanged -= SelectedWorkspaceItems_CollectionChanged;
            _selectedWorkspace.PropertyChanged -= SelectedWorkspace_PropertyChanged;
        }

        _selectedWorkspace = WorkspaceList.SelectedItem as WorkspaceProfile;
        if (_selectedWorkspace is not null)
        {
            _selectedWorkspace.Items.CollectionChanged += SelectedWorkspaceItems_CollectionChanged;
            _selectedWorkspace.PropertyChanged += SelectedWorkspace_PropertyChanged;
        }

        EditorPanel.DataContext = _selectedWorkspace;
        EditorPanel.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        DeleteWorkspaceButton.IsEnabled = _selectedWorkspace is not null && !_isBusy;
        UpdateSequenceControls();
        NewWorkspaceButton.IsEnabled = !_isBusy;
        WorkspaceList.IsEnabled = !_isBusy;
        NoWorkspacesHint.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _selectedWorkspace is null ? "Create a workspace to get started." : $"Editing {_selectedWorkspace.Name}";
        UpdateEmptyState();
        UpdateItemButtons();
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
            ScheduleSave();
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
        if (_dirty && !SaveProfiles(showSuccess: false))
        {
            return;
        }
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
        AppendLog($"Created {profile.Name}. Add launch items.");
        ScheduleSave();
        SaveProfiles(showSuccess: false);
    }

    private void DeleteWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }
        if (_dirty && !SaveProfiles(showSuccess: false))
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
        foreach (LaunchSequence sequence in _configuration.Sequences)
        {
            sequence.WorkspaceIds.Remove(_selectedWorkspace.Id);
        }
        _profiles.Remove(_selectedWorkspace);
        WorkspaceList.SelectedIndex = Math.Min(selectedIndex, _profiles.Count - 1);
        ScheduleSave();
        SaveProfiles(showSuccess: true);
        UpdateSequenceControls();
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

        EditSelectedItem(item);
    }

    private void EditItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchItemsGrid.SelectedItem is LaunchItem item)
        {
            EditSelectedItem(item);
        }
    }

    private void LaunchItemsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LaunchItemsGrid.SelectedItem is LaunchItem item)
        {
            e.Handled = true;
            EditSelectedItem(item);
        }
    }

    private void EditSelectedItem(LaunchItem item)
    {
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
        ScheduleSave();
        AppendLog($"Updated {GetLaunchItemTypeLabel(item.Kind)}: {item.Name}");
    }

    private static string GetLaunchItemTypeLabel(LaunchItemKind kind) => kind switch
    {
        LaunchItemKind.Website => "website",
        LaunchItemKind.File => "file",
        _ => "application"
    };

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
        if (_dirty && !SaveProfiles(showSuccess: false))
        {
            return;
        }
        int desktopCount;
        try
        {
            desktopCount = new DesktopCountService().GetDesktopCount();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not check the available virtual desktops.\n\n{exception.Message}",
                "Desktop detection failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var dialog = new SequenceManagerWindow(
            _configuration.Sequences,
            _profiles,
            desktopCount,
            _configuration.SelectedSequenceId,
            _configuration.DefaultSequenceId)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _configuration.Sequences = new ObservableCollection<LaunchSequence>(dialog.Sequences);
        _configuration.SelectedSequenceId = dialog.SelectedSequenceId;
        _configuration.DefaultSequenceId = dialog.DefaultSequenceId;
        _dirty = true;
        if (!SaveProfiles(showSuccess: true))
        {
            AppendLog("Sequence changes are not saved. Retry saving before closing.");
        }
        else
        {
            AppendLog("Saved launch sequences.");
        }
        RefreshSequenceSelector();
    }

    private void RefreshSequenceSelector()
    {
        _updatingSequenceSelector = true;
        SequenceSelector.ItemsSource = _configuration.Sequences;
        SequenceSelector.SelectedItem = _configuration.Sequences
            .FirstOrDefault(sequence => sequence.Id == _configuration.SelectedSequenceId);
        _updatingSequenceSelector = false;
        UpdateSequenceControls();
    }

    private void SequenceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSequenceSelector)
        {
            return;
        }
        Guid? previous = _configuration.SelectedSequenceId;
        _configuration.SelectedSequenceId = (SequenceSelector.SelectedItem as LaunchSequence)?.Id;
        if (!SaveProfiles(showSuccess: false))
        {
            _configuration.SelectedSequenceId = previous;
            RefreshSequenceSelector();
            return;
        }
        UpdateSequenceControls();
    }

    private void UpdateSequenceControls()
    {
        if (SequenceSelector is null || LaunchSequenceButton is null)
        {
            return;
        }
        SequenceSelector.IsEnabled = !_isBusy && _configuration.Sequences.Count > 0;
        bool hasItems = SequenceSelector.SelectedItem is LaunchSequence sequence && sequence.WorkspaceIds.Count > 0;
        LaunchSequenceButton.IsEnabled = !_isBusy && hasItems;
        LaunchSequenceButton.ToolTip = hasItems
            ? "Launch the selected sequence."
            : "Select a sequence containing at least one workspace. Use Manage sequences to create or edit one.";
        if (SequenceStateText is not null)
        {
            SequenceStateText.Text = hasItems ? "Changes are stored locally in your Windows app-data folder."
                : "Choose or create a sequence with workspaces using Manage sequences.";
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

    private async void CloseWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace is null)
        {
            return;
        }

        WorkspaceProfile profile = _selectedWorkspace;
        var dialog = new CloseWorkspaceWindow(
            profile.Name,
            profile.Items.Where(item => item.Kind is LaunchItemKind.Application or LaunchItemKind.File))
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!SaveProfiles(showSuccess: false))
        {
            return;
        }

        LaunchItem[] selectedItems = dialog.SelectedItems.ToArray();
        await RunCloseOperationAsync(() => _launcher.CloseProfileAsync(profile, selectedItems, ReportActivity));
    }

    private async void LaunchSequenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceSelector.SelectedItem is not LaunchSequence selected || selected.WorkspaceIds.Count == 0 ||
            !SaveProfiles(showSuccess: false))
        {
            return;
        }

        int desktopCount;
        try
        {
            desktopCount = new DesktopCountService().GetDesktopCount();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not check the available virtual desktops.\n\n{exception.Message}",
                "Desktop detection failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if ((selected.EndDesktopIndex is int endIndex && endIndex >= desktopCount) ||
            selected.WorkspaceIds.Any(id => _profiles.First(profile => profile.Id == id).DesktopIndex >= desktopCount))
        {
            MessageBox.Show(this, "A desktop used by this sequence is no longer available. Update its workspace or end-desktop settings before launching.",
                "Desktop unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profilesById = _profiles.ToDictionary(profile => profile.Id);
        WorkspaceProfile[] sequence = selected.WorkspaceIds
            .Select(id => profilesById[id])
            .ToArray();
        await RunLaunchOperationAsync(() => _launcher.LaunchSequenceAsync(
            sequence,
            ReportActivity,
            selected.EndDesktopIndex));
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

    private bool SaveProfiles(bool showSuccess, bool notify = true)
    {
        _saveTimer.Stop();
        LaunchItemsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, exitEditingMode: true);
        LaunchItemsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, exitEditingMode: true);

        if (_profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
        {
            ShowValidationError("Every workspace needs a name.", notify);
            return false;
        }

        if (_profiles.GroupBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            ShowValidationError("Workspace names must be unique.", notify);
            return false;
        }

        if (_profiles.Any(profile => profile.DesktopIndex < 0 || profile.DesktopIndex >= _desktopCount))
        {
            ShowValidationError($"Choose a virtual desktop from 1 to {_desktopCount}.", notify);
            return false;
        }

        foreach (var profile in _profiles)
        {
            foreach (var item in profile.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Target))
                {
                    ShowValidationError($"Every launch item in “{profile.Name}” needs a name and target.", notify);
                    return false;
                }

                if (item.Kind == LaunchItemKind.Website &&
                    (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                {
                    ShowValidationError($"“{item.Name}” must use a valid http:// or https:// address.", notify);
                    return false;
                }
            }
        }

        try
        {
            _store.Save(_configuration);
            _dirty = false;
            SaveStatusText.Text = "Changes saved.";
            SaveButton.Visibility = Visibility.Collapsed;
            if (showSuccess)
            {
                StatusText.Text = "Changes saved.";
                AppendLog("Workspace settings saved.");
            }

            return true;
        }
        catch (Exception exception)
        {
            if (notify) MessageBox.Show(
                $"Could not save workspace settings.\n\n{exception.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Save failed.";
            SaveStatusText.Text = $"Save failed: {exception.Message}";
            SaveButton.Visibility = Visibility.Visible;
            return false;
        }
    }

    private void ShowValidationError(string message, bool notify)
    {
        if (notify) MessageBox.Show(message, "Check workspace settings", MessageBoxButton.OK, MessageBoxImage.Information);
        SaveStatusText.Text = $"Not saved: {message}";
        SaveButton.Visibility = Visibility.Visible;
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
            ActivityExpander.IsExpanded = true;
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

    private async Task RunCloseOperationAsync(Func<Task> close)
    {
        SetBusy(true);
        ActivityLog.Clear();
        _launchHadItemErrors = false;
        _closeHadWarnings = false;
        try
        {
            await close();
            StatusText.Text = _launchHadItemErrors || _closeHadWarnings
                ? "Workspace close finished with warnings or errors."
                : "Workspace close finished.";
            AppendLog(_launchHadItemErrors || _closeHadWarnings
                ? "Workspace close finished with warnings or errors. Review Workspace Activity."
                : "Workspace close finished.");
        }
        catch (Exception exception)
        {
            StatusText.Text = "Workspace close stopped because of an error.";
            ActivityExpander.IsExpanded = true;
            AppendLog($"ERROR: {exception.Message}");
            MessageBox.Show(
                $"The workspace could not be closed.\n\n{exception.Message}",
                "Workspace close failed",
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
        UpdateSequenceControls();
        ConfigureSequenceButton.IsEnabled = !isBusy;
        NewWorkspaceButton.IsEnabled = !isBusy;
        WorkspaceList.IsEnabled = !isBusy;
        DeleteWorkspaceButton.IsEnabled = !isBusy && _selectedWorkspace is not null;
        SaveButton.IsEnabled = !isBusy;
        UpdateItemButtons();
    }

    private void ReportActivity(string message)
    {
        if (message.StartsWith("ERROR:", StringComparison.Ordinal))
        {
            _launchHadItemErrors = true;
            ActivityExpander.IsExpanded = true;
        }
        if (message.StartsWith("WARNING:", StringComparison.Ordinal))
        {
            _closeHadWarnings = true;
            ActivityExpander.IsExpanded = true;
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
        UpdateItemButtons();
        ScheduleSave();
    }

    private void SelectedWorkspace_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceProfile.Name) or nameof(WorkspaceProfile.DesktopIndex))
        {
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        if (!_ready) return;
        _dirty = true;
        SaveStatusText.Text = "Unsaved changes.";
        SaveButton.Visibility = Visibility.Collapsed;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        if (_dirty) SaveProfiles(showSuccess: false, notify: false);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_ready && _dirty && !SaveProfiles(showSuccess: false))
        {
            e.Cancel = true;
        }
    }

    private void LaunchItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateItemButtons();

    private void UpdateItemButtons()
    {
        if (LaunchItemsGrid is null || EditItemButton is null || RemoveItemButton is null ||
            MoveUpButton is null || MoveDownButton is null) return;
        int index = LaunchItemsGrid.SelectedIndex;
        bool selected = !_isBusy && _selectedWorkspace is not null && index >= 0;
        EditItemButton.IsEnabled = RemoveItemButton.IsEnabled = selected;
        MoveUpButton.IsEnabled = selected && index > 0;
        MoveDownButton.IsEnabled = selected && index < _selectedWorkspace!.Items.Count - 1;
    }

    private void UpdateEmptyState()
    {
        EmptyItemsHint.Visibility = _selectedWorkspace is not null && _selectedWorkspace.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
