using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using iRacingLauncher.Models;

namespace iRacingLauncher.ViewModels;

public class AppRowViewModel : INotifyPropertyChanged
{
    public AppEntry Model { get; }

    public AppRowViewModel(AppEntry model)
    {
        Model = model;
    }

    public string Name => Model.Name;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsStartable));
            if (!IsStartable) Selected = false;
        }
    }

    private bool _pathValid = true;
    public bool PathValid
    {
        get => _pathValid;
        set
        {
            if (_pathValid == value) return;
            _pathValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsStartable));
            if (!IsStartable) Selected = false;
        }
    }

    public bool Selected
    {
        get => Model.Selected;
        set
        {
            if (Model.Selected == value) return;
            Model.Selected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Start is offered only for an app that exists on disk and isn't already up.</summary>
    public bool IsStartable => PathValid && !IsRunning;

    public string StatusText => !PathValid ? "Path not found" : IsRunning ? "Running" : "Not running";

    public Brush StatusColor => !PathValid
        ? new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23))
        : IsRunning
            ? new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F))
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
