using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WorkspaceLauncher.Models;

public enum LaunchItemKind
{
    Application,
    Website
}

public sealed class LaunchItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _target = "";
    private string _processName = "";
    private string _windowTitle = "";

    public LaunchItemKind Kind { get; set; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Target
    {
        get => _target;
        set => SetField(ref _target, value);
    }

    public string ProcessName
    {
        get => _processName;
        set => SetField(ref _processName, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetField(ref _windowTitle, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
