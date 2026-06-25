using System.Windows.Media;
using CryptoTicker.Core;
using Forms = System.Windows.Forms;

namespace CryptoTicker.Desktop;

public partial class MainWindow
{
    private void PickColor(bool up)
    {
        using var dialog = new Forms.ColorDialog();
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (up) _settings.UpColor = color; else _settings.DownColor = color;
        SettingsStore.Save(_settings);
        ApplyColors();
    }

    private void ApplyColors()
    {
        UpColorButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.UpColor));
        DownColorButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.DownColor));
    }

    private string ColorForDirection(Direction direction) => direction switch
    {
        Direction.Up => _settings.UpColor,
        Direction.Down => _settings.DownColor,
        _ => "#6B7280"
    };

    private static System.Windows.Media.Brush BrushForColor(string color) => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private System.Windows.Media.Brush BrushForDirection(Direction direction) => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColorForDirection(direction)));
}
