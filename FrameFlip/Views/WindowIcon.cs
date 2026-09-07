using System.Windows;
using System.Windows.Media.Imaging;

namespace FrameFlip.Views;

/// <summary>
/// Jedem Fenster sein Symbol.
///
/// WPF nimmt nicht von sich aus das Symbol der Programmdatei - ein Fenster ohne
/// eigenes Icon zeigt das leere Blatt von Windows, und zwar in der Titelleiste, im
/// Umschalter und in der Taskleiste. Eintragen laesst es sich in jeder Fensterdatei
/// einzeln; vergessen wuerde man dabei genau das eine, das man selten oeffnet.
/// Deshalb einmal zentral, beim Laden.
/// </summary>
public static class WindowIcon
{
    private static BitmapFrame? _icon;

    public static void Apply(Window window)
    {
        try
        {
            if (window.Icon is not null) return;

            _icon ??= BitmapFrame.Create(
                new Uri("pack://application:,,,/FrameFlip;component/Assets/FrameFlip.ico"),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            window.Icon = _icon;
        }
        catch (Exception)
        {
            // Ohne Symbol sieht es schlechter aus. Das ist alles.
        }
    }
}
