using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace iRacingLauncher;

/// <summary>
/// The launcher's look: light or dark, always with its own blue accent. WPF-UI
/// otherwise takes the Windows accent colour, which on many PCs is a grey that
/// makes selected rows, checkboxes and primary buttons look disabled.
/// </summary>
public static class AppTheme
{
    public static readonly Color Accent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    public static void Apply(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, updateAccent: false);
        ApplicationAccentColorManager.Apply(Accent, theme);
    }
}
