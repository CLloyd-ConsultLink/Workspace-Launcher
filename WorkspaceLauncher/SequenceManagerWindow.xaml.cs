using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher;

public partial class SequenceManagerWindow : Window
{
    private readonly ObservableCollection<LaunchSequence> _sequences;
    private readonly IReadOnlyList<WorkspaceProfile> _workspaces;
    private readonly int _desktopCount;

    public IReadOnlyList<LaunchSequence> Sequences => _sequences.ToArray();
    public Guid? SelectedSequenceId => (SequenceList.SelectedItem as LaunchSequence)?.Id;
    public Guid? DefaultSequenceId { get; private set; }

    public SequenceManagerWindow(IEnumerable<LaunchSequence> sequences, IEnumerable<WorkspaceProfile> workspaces,
        int desktopCount, Guid? selectedId, Guid? defaultId = null)
    {
        InitializeComponent();
        _workspaces = workspaces.ToArray();
        _desktopCount = desktopCount;
        DefaultSequenceId = defaultId;
        _sequences = new ObservableCollection<LaunchSequence>(sequences.Select(sequence => new LaunchSequence
        {
            Id = sequence.Id,
            Name = sequence.Name,
            WorkspaceIds = new ObservableCollection<Guid>(sequence.WorkspaceIds),
            EndDesktopIndex = sequence.EndDesktopIndex
        }));
        SequenceList.ItemsSource = _sequences;
        SequenceList.SelectedItem = _sequences.FirstOrDefault(sequence => sequence.Id == selectedId);
        UpdateButtons();
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        EditSequence(new LaunchSequence(), isNew: true);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceList.SelectedItem is LaunchSequence sequence)
        {
            EditSequence(sequence, isNew: false);
        }
    }

    private void EditSequence(LaunchSequence sequence, bool isNew)
    {
        var dialog = new SequenceWindow(_workspaces, sequence.WorkspaceIds, _desktopCount, sequence.EndDesktopIndex,
            sequence.Name, _sequences.Where(other => other.Id != sequence.Id).Select(other => other.Name))
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        sequence.Name = dialog.SequenceName;
        sequence.WorkspaceIds = new ObservableCollection<Guid>(dialog.SequenceIds);
        sequence.EndDesktopIndex = dialog.EndDesktopIndex;
        if (isNew)
        {
            _sequences.Add(sequence);
        }
        SequenceList.SelectedItem = sequence;
        UpdateButtons();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceList.SelectedItem is not LaunchSequence sequence ||
            MessageBox.Show(this, $"Delete the “{sequence.Name}” sequence? Workspaces will not be deleted.",
                "Delete sequence", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        int index = SequenceList.SelectedIndex;
        if (DefaultSequenceId == sequence.Id)
        {
            DefaultSequenceId = null;
        }
        _sequences.Remove(sequence);
        if (_sequences.Count > 0)
        {
            SequenceList.SelectedIndex = Math.Min(index, _sequences.Count - 1);
        }
        UpdateButtons();
    }

    private void SequenceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceList.SelectedItem is LaunchSequence sequence)
        {
            DefaultSequenceId = DefaultSequenceId == sequence.Id ? null : sequence.Id;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        if (EditButton is not null && DeleteButton is not null && DefaultButton is not null && DefaultLabel is not null)
        {
            EditButton.IsEnabled = DeleteButton.IsEnabled = SequenceList.SelectedItem is LaunchSequence;
            DefaultButton.IsEnabled = SequenceList.SelectedItem is LaunchSequence;
            DefaultButton.Content = (SequenceList.SelectedItem as LaunchSequence)?.Id == DefaultSequenceId && DefaultSequenceId is not null
                ? "Clear default" : "Set as default";
            DefaultLabel.Text = $"Default: {_sequences.FirstOrDefault(sequence => sequence.Id == DefaultSequenceId)?.Name ?? "None"}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
