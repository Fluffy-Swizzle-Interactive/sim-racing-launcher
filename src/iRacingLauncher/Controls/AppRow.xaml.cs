using System;
using System.Windows;
using System.Windows.Controls;
using iRacingLauncher.ViewModels;

namespace iRacingLauncher.Controls;

public partial class AppRow : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(AppRowViewModel), typeof(AppRow),
        new PropertyMetadata(null, OnViewModelChanged));

    public AppRowViewModel? ViewModel
    {
        get => (AppRowViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public event EventHandler<AppRowViewModel>? StartRequested;
    public event EventHandler<AppRowViewModel>? StopRequested;

    public AppRow()
    {
        InitializeComponent();
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (AppRow)d;
        row.DataContext = e.NewValue;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            StartRequested?.Invoke(this, ViewModel);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            StopRequested?.Invoke(this, ViewModel);
        }
    }
}
