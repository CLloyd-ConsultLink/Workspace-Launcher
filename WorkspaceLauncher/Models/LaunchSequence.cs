using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WorkspaceLauncher.Models;

public sealed class LaunchSequence : INotifyPropertyChanged
{
    private string _name = "";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    public ObservableCollection<Guid> WorkspaceIds { get; set; } = [];

    public int? EndDesktopIndex { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
