using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class SequenceWindow : Window
{
    private readonly ObservableCollection<WorkspaceProfile> _available = [];
    private readonly ObservableCollection<WorkspaceProfile> _sequence = [];
    private readonly HashSet<string> _otherNames;
    private readonly int _desktopCount;

    public IReadOnlyList<Guid> SequenceIds => _sequence.Select(profile => profile.Id).ToList();

    public string SequenceName => SequenceNameBox.Text.Trim();

    public int? EndDesktopIndex => (EndDesktopComboBox.SelectedItem as DesktopChoice)?.Index;

    public SequenceWindow(
        IEnumerable<WorkspaceProfile> workspaces,
        IEnumerable<Guid> sequenceIds,
        int desktopCount,
        int? endDesktopIndex,
        string name,
        IEnumerable<string> otherNames)
    {
        InitializeComponent();
        _desktopCount = desktopCount;
        _otherNames = new HashSet<string>(otherNames.Select(value => value.Trim()), StringComparer.OrdinalIgnoreCase);
        SequenceNameBox.Text = name;

        EndDesktopComboBox.DisplayMemberPath = nameof(DesktopChoice.Label);
        EndDesktopComboBox.Items.Add(new DesktopChoice(null, "Last workspace's desktop"));
        for (int desktopIndex = 0; desktopIndex < desktopCount; desktopIndex++)
        {
            EndDesktopComboBox.Items.Add(new DesktopChoice(desktopIndex, $"Desktop {desktopIndex + 1}"));
        }
        if (endDesktopIndex is int unavailable && unavailable >= desktopCount)
        {
            EndDesktopComboBox.Items.Add(new DesktopChoice(unavailable, $"Desktop {unavailable + 1} (unavailable)"));
        }
        EndDesktopComboBox.SelectedItem = EndDesktopComboBox.Items.Cast<DesktopChoice>()
            .First(choice => choice.Index == endDesktopIndex);

        WorkspaceProfile[] profiles = workspaces.ToArray();
        Guid[] orderedIds = sequenceIds.ToArray();
        var profilesById = profiles.ToDictionary(profile => profile.Id);
        var selectedIds = orderedIds.ToHashSet();
        foreach (Guid id in orderedIds)
        {
            _sequence.Add(profilesById[id]);
        }
        foreach (WorkspaceProfile profile in profiles)
        {
            if (!selectedIds.Contains(profile.Id))
            {
                _available.Add(profile);
            }
        }

        AvailableList.ItemsSource = _available;
        SequenceList.ItemsSource = _sequence;
        UpdateButtons();
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
        UpdateButtons();
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
        UpdateButtons();
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
        UpdateButtons();
    }

    private void Lists_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (AddButton is null || RemoveButton is null || MoveUpButton is null || MoveDownButton is null ||
            AvailableList is null || SequenceList is null)
        {
            return;
        }
        int index = SequenceList.SelectedIndex;
        AddButton.IsEnabled = AvailableList.SelectedItem is not null;
        RemoveButton.IsEnabled = index >= 0;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _sequence.Count - 1;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SequenceName) || _otherNames.Contains(SequenceName))
        {
            MessageBox.Show(this, "Enter a unique name for this sequence.", "Check sequence name",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (EndDesktopIndex >= _desktopCount)
        {
            MessageBox.Show(this, "Choose an available end desktop.", "Desktop unavailable",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private sealed record DesktopChoice(int? Index, string Label);
}
