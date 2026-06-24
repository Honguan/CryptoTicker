using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryptoTicker.Core;

namespace CryptoTicker.Desktop;

public partial class MainWindow : Window
{
    private readonly MarketDataService _marketData = new();
    private readonly Dictionary<string, AnalysisResult> _analyses = new();
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppSettings _settings = new();
    private decimal[] _chartValues = [];
    private string _direction = "等待分析";
    private bool _analysisRequested;

    public MainWindow()
    {
        InitializeComponent();
        _marketData.QuotesChanged += UpdateQuote;
        _marketData.StatusChanged += status => Dispatcher.BeginInvoke(() => StatusText.Text = status);
        _displayTimer.Tick += (_, _) => UpdateQuote();
        ChartCanvas.SizeChanged += (_, _) => DrawChart();
    }

    public event Action<string, decimal?, string>? PriceChanged;

    public void Stop()
    {
        _displayTimer.Stop();
        _marketData.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _settings = SettingsStore.Load();
        PairBox.Text = _settings.Pair;
        AiEndpointBox.Text = _settings.AiEndpoint;
        AiModelBox.Text = _settings.AiModel;
        AiCredentialBox.Text = _settings.AiCredentialTarget;
        LoadCustomSource();
        _displayTimer.Start();
        await RestartAsync();
    }

    private async void OnApply(object sender, RoutedEventArgs eventArgs)
    {
        _settings.Pair = NormalizePair(PairBox.Text);
        PairBox.Text = _settings.Pair;
        SettingsStore.Save(_settings);
        await RestartAsync();
    }

    private async void OnRefreshAnalysis(object sender, RoutedEventArgs eventArgs) => await RefreshAnalysisAsync();

    private async void OnGenerateAi(object sender, RoutedEventArgs eventArgs)
    {
        if (_analyses.Count == 0)
        {
            await RefreshAnalysisAsync();
        }

        AiText.Text = "正在產生 AI 解讀…";
        AiText.Text = await AiAnalysisService.GenerateAsync(_settings, _analyses, CancellationToken.None);
    }

    private void OnSaveCustomSource(object sender, RoutedEventArgs eventArgs)
    {
        var source = new CustomSourceSettings
        {
            Name = CustomNameBox.Text.Trim(),
            Url = CustomUrlBox.Text.Trim(),
            PricePath = CustomPricePathBox.Text.Trim(),
            TimestampPath = CustomTimestampPathBox.Text.Trim(),
            SecretHeaderName = CustomHeaderBox.Text.Trim(),
            CredentialTarget = CustomCredentialBox.Text.Trim()
        };
        if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Url) || string.IsNullOrWhiteSpace(source.PricePath))
        {
            StatusText.Text = "自訂來源需要名稱、網址與價格 JSON 路徑。";
            return;
        }

        if (!string.IsNullOrEmpty(CustomSecretBox.Password))
        {
            CredentialStore.Save(source.CredentialTarget, CustomSecretBox.Password);
            CustomSecretBox.Clear();
        }

        _settings.CustomSources.RemoveAll(item => string.Equals(item.Name, source.Name, StringComparison.OrdinalIgnoreCase));
        _settings.CustomSources.Add(source);
        SettingsStore.Save(_settings);
        StatusText.Text = "已儲存自訂來源。重新套用交易對後生效。";
    }

    private void OnSaveAiSettings(object sender, RoutedEventArgs eventArgs)
    {
        _settings.AiEndpoint = AiEndpointBox.Text.Trim();
        _settings.AiModel = AiModelBox.Text.Trim();
        _settings.AiCredentialTarget = AiCredentialBox.Text.Trim();
        if (!string.IsNullOrEmpty(AiSecretBox.Password))
        {
            CredentialStore.Save(_settings.AiCredentialTarget, AiSecretBox.Password);
            AiSecretBox.Clear();
        }

        SettingsStore.Save(_settings);
        StatusText.Text = "已儲存 AI 設定。";
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!((App)System.Windows.Application.Current).IsExiting)
        {
            eventArgs.Cancel = true;
            Hide();
        }
    }

    private async Task RestartAsync()
    {
        _analyses.Clear();
        _analysisRequested = false;
        AnalysisList.Items.Clear();
        _chartValues = [];
        DrawChart();
        await _marketData.StartAsync(_settings);
        StatusText.Text = "正在連線行情來源…";
        await RefreshAnalysisAsync();
    }

    private async Task RefreshAnalysisAsync()
    {
        if (_marketData.Aggregate().Price is null)
        {
            AnalysisList.Items.Clear();
            AnalysisList.Items.Add("資料不足，尚未產生分析。");
            return;
        }

        _analysisRequested = true;
        AnalysisList.Items.Clear();
        _analyses.Clear();
        foreach (var timeframe in new[] { "15m", "1h", "4h" })
        {
            try
            {
                var closes = await CandleService.GetAsync(_settings.Pair, timeframe, CancellationToken.None);
                var analysis = MarketAnalyzer.Analyze(closes);
                _analyses[timeframe] = analysis;
                AnalysisList.Items.Add($"{timeframe}：{DirectionText(analysis.Direction)}｜上漲 {analysis.UpProbability}%｜RSI {analysis.Rsi:F1}");
                if (timeframe == "1h")
                {
                    _chartValues = closes;
                    _direction = DirectionText(analysis.Direction);
                    DrawChart();
                }
            }
            catch (Exception exception)
            {
                AnalysisList.Items.Add($"{timeframe}：分析失敗（{exception.Message}）");
            }
        }

        UpdateQuote();
    }

    private void UpdateQuote()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateQuote);
            return;
        }

        var aggregate = _marketData.Aggregate();
        PriceText.Text = aggregate.Price is null ? "資料不足" : aggregate.Price.Value.ToString("N4");
        StatusText.Text = aggregate.Price is null ? $"有效來源 {aggregate.ActiveSourceCount}/2" : $"整合 {aggregate.ActiveSourceCount} 個來源";
        SourceList.Items.Clear();
        foreach (var source in _marketData.Sources())
        {
            var updatedAt = source.LastUpdatedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "無報價";
            var error = source.Health == SourceHealth.Error ? $"：{source.LastError}" : string.Empty;
            SourceList.Items.Add($"{source.Source}｜{SourceHealthText(source.Health)}｜{updatedAt}{error}");
        }
        PriceChanged?.Invoke(_settings.Pair, aggregate.Price, _direction);
        if (aggregate.Price is not null && !_analysisRequested)
        {
            _ = RefreshAnalysisAsync();
        }
    }

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

    private void LoadCustomSource()
    {
        var source = _settings.CustomSources.FirstOrDefault();
        if (source is null)
        {
            return;
        }

        CustomNameBox.Text = source.Name;
        CustomUrlBox.Text = source.Url;
        CustomPricePathBox.Text = source.PricePath;
        CustomTimestampPathBox.Text = source.TimestampPath;
        CustomHeaderBox.Text = source.SecretHeaderName;
        CustomCredentialBox.Text = source.CredentialTarget;
    }

    private static string NormalizePair(string pair)
    {
        var parts = pair.Trim().ToUpperInvariant().Replace('-', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? $"{parts[0]}/{parts[1]}" : "BTC/USDT";
    }

    private static string DirectionText(Direction direction) => direction switch
    {
        Direction.Up => "上漲",
        Direction.Down => "下跌",
        _ => "中性"
    };

    private static string SourceHealthText(SourceHealth health) => health switch
    {
        SourceHealth.Fresh => "新鮮",
        SourceHealth.Error => "錯誤",
        _ => "過期"
    };
}
