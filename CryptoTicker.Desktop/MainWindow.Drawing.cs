using System.Windows.Shapes;

namespace CryptoTicker.Desktop;

public partial class MainWindow
{
    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        if (_chartValues.Length < 2 || ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
        {
            return;
        }

        var minimum = _chartValues.Min();
        var maximum = _chartValues.Max();
        var range = maximum - minimum;
        if (range == 0)
        {
            range = 1;
        }

        var line = new Polyline { Stroke = System.Windows.Media.Brushes.DodgerBlue, StrokeThickness = 2 };
        for (var index = 0; index < _chartValues.Length; index++)
        {
            var x = index * ChartCanvas.ActualWidth / (_chartValues.Length - 1);
            var y = ChartCanvas.ActualHeight - (double)((_chartValues[index] - minimum) / range) * ChartCanvas.ActualHeight;
            line.Points.Add(new System.Windows.Point(x, y));
        }

        ChartCanvas.Children.Add(line);
    }
}
