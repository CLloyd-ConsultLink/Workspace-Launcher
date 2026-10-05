using System.Collections.ObjectModel;

namespace WorkspaceLauncher.Models;

public sealed class WorkspaceConfiguration
{
    public ObservableCollection<WorkspaceProfile> Workspaces { get; set; } = [];

    public ObservableCollection<Guid> LaunchSequence { get; set; } = [];
}
