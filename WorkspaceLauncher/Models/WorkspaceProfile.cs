using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WorkspaceLauncher.Models;

public sealed class WorkspaceProfile : INotifyPropertyChanged
{
    private string _name = "";
    private int _desktopIndex;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int DesktopIndex
    {
        get => _desktopIndex;
        set
        {
            if (SetField(ref _desktopIndex, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DesktopLabel)));
            }
        }
    }

    public string DesktopLabel => $"Desktop {DesktopIndex + 1}";

    public ObservableCollection<LaunchItem> Items { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
