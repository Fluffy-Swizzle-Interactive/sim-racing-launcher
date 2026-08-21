using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;

namespace iRacingLauncher;

/// <summary>
/// The title bar mark comes in a dark-ground and a light-ground variant (see
/// LOGOS/README.md) — this picks whichever matches the app's current theme so it
/// stays legible when the user switches themes.
/// </summary>
internal static class TitleBarIconSource
{
    public static ImageSource ForTheme(ApplicationTheme theme)
    {
        var fileName = theme == ApplicationTheme.Light ? "titlebar-icon-light.png" : "titlebar-icon-dark.png";
        return new BitmapImage(new Uri($"pack://application:,,,/Assets/{fileName}"));
    }
}
