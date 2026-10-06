using System.Collections.ObjectModel;
using System.Windows;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class SequenceWindow : Window
{
    private readonly ObservableCollection<WorkspaceProfile> _available = [];
    private readonly ObservableCollection<WorkspaceProfile> _sequence = [];

    public IReadOnlyList<Guid> SequenceIds => _sequence.Select(profile => profile.Id).ToList();

    public int? EndDesktopIndex => EndDesktopComboBox.SelectedIndex <= 0
        ? null
        : EndDesktopComboBox.SelectedIndex - 1;

    public SequenceWindow(
        IEnumerable<WorkspaceProfile> workspaces,
        IEnumerable<Guid> sequenceIds,
        int desktopCount,
        int? endDesktopIndex)
    {
        InitializeComponent();

        EndDesktopComboBox.Items.Add("Last workspace's desktop");
        for (int desktopIndex = 0; desktopIndex < desktopCount; desktopIndex++)
        {
            EndDesktopComboBox.Items.Add($"Desktop {desktopIndex + 1}");
        }
        EndDesktopComboBox.SelectedIndex = endDesktopIndex is int selectedDesktop &&
                                           selectedDesktop >= 0 &&
                                           selectedDesktop < desktopCount
            ? selectedDesktop + 1
            : 0;

        WorkspaceProfile[] profiles = workspaces.ToArray();
        var selectedIds = sequenceIds.ToHashSet();
        foreach (WorkspaceProfile profile in profiles)
        {
            if (selectedIds.Contains(profile.Id))
            {
                _sequence.Add(profile);
            }
            else
            {
                _available.Add(profile);
            }
        }

        AvailableList.ItemsSource = _available;
        SequenceList.ItemsSource = _sequence;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not WorkspaceProfile profile)
        {
            return;
        }

        _available.Remove(profile);
        _sequence.Add(profile);
        SequenceList.SelectedItem = profile;
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceList.SelectedItem is not WorkspaceProfile profile)
        {
            return;
        }

        _sequence.Remove(profile);
        _available.Add(profile);
        AvailableList.SelectedItem = profile;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        if (SequenceList.SelectedItem is not WorkspaceProfile profile)
        {
            return;
        }

        int oldIndex = _sequence.IndexOf(profile);
        int newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= _sequence.Count)
        {
            return;
        }

        _sequence.Move(oldIndex, newIndex);
        SequenceList.SelectedItem = profile;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
