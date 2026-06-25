using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryptoTicker.Core;
using Forms = System.Windows.Forms;

namespace CryptoTicker.Desktop;

public partial class MainWindow : Window
{
    private readonly MarketDataService _marketData = new();
    private readonly WatchlistService _watchlist = new();
    private readonly Dictionary<string, AnalysisResult> _analyses = new();
    private readonly Dictionary<string, decimal> _watchPrices = new();
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppSettings _settings = new();
    private decimal[] _chartValues = [];
    private string _direction = "等待分析";
    private decimal? _previousPrice;
    private Direction? _lastSignal;
    private readonly DispatcherTimer _analysisTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _analysisRequested;
    private bool _isRestarting;
    private bool _isRefreshingAnalysis;
    private bool _isGeneratingAi;

    public MainWindow()
    {
        InitializeComponent();
        _marketData.QuotesChanged += UpdateQuote;
        _watchlist.QuotesChanged += UpdateQuote;
        _marketData.StatusChanged += status => Dispatcher.BeginInvoke(() => StatusText.Text = status);
        _displayTimer.Tick += (_, _) => UpdateQuote();
        _analysisTimer.Tick += async (_, _) => await RefreshAnalysisAsync();
        ChartCanvas.SizeChanged += (_, _) => DrawChart();
    }

    public event Action<string, decimal?, string, string>? PriceChanged;

    public void Stop()
    {
        _displayTimer.Stop();
        _analysisTimer.Stop();
        _marketData.Dispose();
        _watchlist.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _settings = SettingsStore.Load();
        _settings.WatchPairs = WatchPairList.Parse(string.Join(',', _settings.WatchPairs), _settings.Pair).ToList();
        PairBox.Text = _settings.Pair;
        WatchPairsBox.Text = string.Join(',', _settings.WatchPairs);
        AiEndpointBox.Text = _settings.AiEndpoint;
        AiModelBox.Text = _settings.AiModel;
        AiCredentialBox.Text = _settings.AiCredentialTarget;
        LoadCustomSource();
        _displayTimer.Start();
        _analysisTimer.Start();
        RenderAlerts();
        ApplyColors();
        await RestartAsync();
    }

    private async void OnApply(object sender, RoutedEventArgs eventArgs)
    {
        _settings.Pair = NormalizePair(PairBox.Text);
        PairBox.Text = _settings.Pair;
        _settings.WatchPairs = WatchPairList.Parse(WatchPairsBox.Text, _settings.Pair).ToList();
        WatchPairsBox.Text = string.Join(',', _settings.WatchPairs);
        SettingsStore.Save(_settings);
        await RestartAsync();
    }

    private async void OnRefreshAnalysis(object sender, RoutedEventArgs eventArgs) => await RefreshAnalysisAsync();

    private async void OnGenerateAi(object sender, RoutedEventArgs eventArgs)
    {
        if (_isGeneratingAi)
        {
            return;
        }

        _isGeneratingAi = true;
        UpdateOperationControls();
        try
        {
            if (_analyses.Count == 0)
            {
                await RefreshAnalysisAsync();
            }

            AiText.Text = "正在產生 AI 解讀…";
            AiText.Text = await AiAnalysisService.GenerateAsync(_settings, _analyses, CancellationToken.None);
        }
        finally
        {
            _isGeneratingAi = false;
            UpdateOperationControls();
        }
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

    private void OnAddAlert(object sender, RoutedEventArgs eventArgs)
    {
        if (!decimal.TryParse(AlertTargetBox.Text, out var target) || target <= 0)
        {
            StatusText.Text = "請輸入有效目標價。";
            return;
        }

        var direction = AlertDirectionBox.SelectedIndex == 0 ? AlertDirection.Above : AlertDirection.Below;
        _settings.Alerts.Add(new PriceAlert(_settings.Pair, direction, target));
        SettingsStore.Save(_settings);
        AlertTargetBox.Clear();
        RenderAlerts();
    }

    private void OnDeleteAlert(object sender, RoutedEventArgs eventArgs)
    {
        if (AlertList.SelectedItem is PriceAlert alert)
        {
            _settings.Alerts.Remove(alert);
            SettingsStore.Save(_settings);
            RenderAlerts();
        }
    }

    private void OnPickUpColor(object sender, RoutedEventArgs eventArgs) => PickColor(true);

    private void OnPickDownColor(object sender, RoutedEventArgs eventArgs) => PickColor(false);

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
        if (_isRestarting)
        {
            return;
        }

        _isRestarting = true;
        UpdateOperationControls();
        try
        {
            _analyses.Clear();
            _previousPrice = null;
            _lastSignal = null;
            _analysisRequested = false;
            _watchPrices.Clear();
            AnalysisList.Items.Clear();
            _chartValues = [];
            DrawChart();
            await _marketData.StartAsync(_settings);
            await _watchlist.StartAsync(_settings.WatchPairs);
            StatusText.Text = "正在連線行情來源…";
            await RefreshAnalysisAsync();
        }
        finally
        {
            _isRestarting = false;
            UpdateOperationControls();
        }
    }

    private async Task RefreshAnalysisAsync()
    {
        if (_isRefreshingAnalysis)
        {
            return;
        }

        _isRefreshingAnalysis = true;
        UpdateOperationControls();
        try
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
                        if (_lastSignal is not null && SignalTransition.ShouldNotify(_lastSignal.Value, analysis.Direction))
                        {
                            ((App)System.Windows.Application.Current).Notify("技術方向轉換", $"{_settings.Pair}：{DirectionText(analysis.Direction)}");
                        }

                        _lastSignal = analysis.Direction;
                        AnalysisList.Foreground = BrushForDirection(analysis.Direction);
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
        finally
        {
            _isRefreshingAnalysis = false;
            UpdateOperationControls();
        }
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
        RenderWatchList();
        PriceChanged?.Invoke(_settings.Pair, aggregate.Price, _direction, ColorForDirection(_lastSignal ?? Direction.Neutral));
        if (aggregate.Price is decimal price)
        {
            if (_previousPrice is decimal previous)
            {
                foreach (var alert in _settings.Alerts.Where(alert => alert.Pair == _settings.Pair && AlertEvaluator.Crossed(alert, previous, price)))
                {
                    ((App)System.Windows.Application.Current).Notify("價格警示", $"{alert.Pair} {AlertText(alert)}：{price:N4}");
                }
            }

            _previousPrice = price;
        }
        if (aggregate.Price is not null && !_analysisRequested)
        {
            _ = RefreshAnalysisAsync();
        }
    }

    private void RenderWatchList()
    {
        WatchList.Items.Clear();
        foreach (var snapshot in _watchlist.Snapshots())
        {
            var price = snapshot.Aggregate.Price;
            var previous = _watchPrices.GetValueOrDefault(snapshot.Pair);
            var direction = price is null || previous == 0 ? "●" : price > previous ? "▲" : price < previous ? "▼" : "●";
            var color = price is null ? "#6B7280" : price > previous ? _settings.UpColor : price < previous ? _settings.DownColor : "#6B7280";
            var updatedAt = snapshot.Sources
                .Where(source => source.LastUpdatedAt is not null)
                .Select(source => source.LastUpdatedAt!.Value)
                .DefaultIfEmpty()
                .Max();
            var updatedText = updatedAt == default ? "無報價" : updatedAt.ToLocalTime().ToString("HH:mm:ss");
            var status = price is null ? $"資料不足 {snapshot.Aggregate.ActiveSourceCount}/2" : $"整合 {snapshot.Aggregate.ActiveSourceCount} 個來源";
            WatchList.Items.Add(new ListBoxItem
            {
                Content = $"{direction} {snapshot.Pair}｜{(price is null ? "資料不足" : price.Value.ToString("N4"))}｜{status}｜{updatedText}",
                Foreground = BrushForColor(color)
            });

            if (price is not null)
            {
                _watchPrices[snapshot.Pair] = price.Value;
            }
        }
    }

    private void UpdateOperationControls()
    {
        ApplyButton.IsEnabled = !_isRestarting;
        RefreshAnalysisButton.IsEnabled = !_isRestarting && !_isRefreshingAnalysis;
        GenerateAiButton.IsEnabled = !_isRestarting && !_isGeneratingAi;
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

    private void RenderAlerts()
    {
        AlertList.Items.Clear();
        foreach (var alert in _settings.Alerts)
        {
            AlertList.Items.Add(alert);
        }
    }

    private static string NormalizePair(string pair)
    {
        return WatchPairList.Normalize(pair) ?? "BTC/USDT";
    }

    private static string DirectionText(Direction direction) => direction switch
    {
        Direction.Up => "▲ 上漲",
        Direction.Down => "▼ 下跌",
        _ => "● 中性"
    };

    private static string AlertText(PriceAlert alert) => alert.Direction == AlertDirection.Above ? "上破" : "下破";

    private static string SourceHealthText(SourceHealth health) => health switch
    {
        SourceHealth.Fresh => "新鮮",
        SourceHealth.Error => "錯誤",
        _ => "過期"
    };
}
