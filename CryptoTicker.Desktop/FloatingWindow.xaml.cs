using System.Windows;
using System.Windows.Input;

namespace CryptoTicker.Desktop;

public partial class FloatingWindow : Window
{
    public FloatingWindow() => InitializeComponent();

    public event Action? OpenRequested;

    public void SetQuote(string pair, decimal? price, string direction)
    {
        PairText.Text = pair;
        PriceText.Text = price is null ? "資料不足" : price.Value.ToString("N4");
        DirectionText.Text = direction;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ClickCount == 2)
        {
            OpenRequested?.Invoke();
            return;
        }

        DragMove();
    }
}
