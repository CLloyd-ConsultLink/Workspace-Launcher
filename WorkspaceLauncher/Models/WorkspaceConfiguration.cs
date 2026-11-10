using System.Collections.ObjectModel;

namespace WorkspaceLauncher.Models;

public sealed class WorkspaceConfiguration
{
    public ObservableCollection<WorkspaceProfile> Workspaces { get; set; } = [];

    public ObservableCollection<LaunchSequence> Sequences { get; set; } = [];

    public Guid? SelectedSequenceId { get; set; }

    public Guid? DefaultSequenceId { get; set; }
}
