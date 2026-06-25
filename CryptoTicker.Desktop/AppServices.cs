using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CryptoTicker.Core;

namespace CryptoTicker.Desktop;

public sealed class MarketDataService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConcurrentDictionary<string, SourceState> _sources = new();
    private CancellationTokenSource? _cancellation;

    public event Action? QuotesChanged;
    public event Action<string>? StatusChanged;

    public Task StartAsync(AppSettings settings, bool includeCustomSources = true)
    {
        Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var pair = settings.Pair;
        foreach (var exchange in new[] { "Binance", "OKX", "Bybit" })
        {
            _sources.TryAdd(exchange, new SourceState(exchange));
            _ = RunExchangeAsync(exchange, pair, token);
        }
        foreach (var source in includeCustomSources ? settings.CustomSources.Where(source => !string.IsNullOrWhiteSpace(source.Url) && !string.IsNullOrWhiteSpace(source.PricePath)) : Enumerable.Empty<CustomSourceSettings>())
        {
            _sources.TryAdd(source.Name, new SourceState(source.Name));
            _ = RunCustomSourceAsync(source, token);
        }

        return Task.CompletedTask;
    }

    public AggregationResult Aggregate()
    {
        var now = DateTimeOffset.UtcNow;
        return QuoteAggregator.Aggregate(_sources.Values
            .Select(source => source.Snapshot(now))
            .Where(source => source.LastPrice is not null && source.LastUpdatedAt is not null)
            .Select(source => new Quote(source.Source, source.LastPrice!.Value, source.LastUpdatedAt!.Value)), now);
    }

    public IReadOnlyList<SourceSnapshot> Sources()
    {
        var now = DateTimeOffset.UtcNow;
        return _sources.Values.Select(source => source.Snapshot(now)).OrderBy(source => source.Source).ToArray();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _sources.Clear();
    }

    private async Task RunExchangeAsync(string exchange, string pair, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(ExchangeUri(exchange, pair), token);
                var subscription = Subscription(exchange, pair);
                if (subscription is not null)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(subscription), WebSocketMessageType.Text, true, token);
                }

                StatusChanged?.Invoke($"{exchange} 已連線");
                var buffer = new byte[8192];
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var price = ReadTicker(exchange, Encoding.UTF8.GetString(message.ToArray()));
                    if (price is not null)
                    {
                        Update(exchange, price.Value);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Fail(exchange, exception.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCustomSourceAsync(CustomSourceSettings source, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                var secret = CredentialStore.Read(source.CredentialTarget);
                if (!string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(source.SecretHeaderName))
                {
                    request.Headers.TryAddWithoutValidation(source.SecretHeaderName, secret);
                }

                using var response = await Http.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(token);
                var price = JsonPathReader.ReadDecimal(json, source.PricePath);
                if (price is null)
                {
                    Fail(source.Name, "找不到有效價格欄位。");
                }
                else
                {
                    var updatedAt = string.IsNullOrWhiteSpace(source.TimestampPath)
                        ? DateTimeOffset.UtcNow
                        : JsonPathReader.ReadTimestamp(json, source.TimestampPath) ?? DateTimeOffset.UtcNow;
                    Update(source.Name, price.Value, updatedAt);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Fail(source.Name, exception.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(token));
    }

    private void Update(string source, decimal price, DateTimeOffset? updatedAt = null)
    {
        var timestamp = updatedAt ?? DateTimeOffset.UtcNow;
        var state = _sources.GetOrAdd(source, name => new SourceState(name));
        if (price <= 0 || timestamp > DateTimeOffset.UtcNow)
        {
            state.RecordFailure("收到無效報價或未來時間戳記。");
            QuotesChanged?.Invoke();
            return;
        }

        state.RecordSuccess(price, timestamp);
        QuotesChanged?.Invoke();
    }

    private void Fail(string source, string error)
    {
        _sources.GetOrAdd(source, name => new SourceState(name)).RecordFailure(error);
        StatusChanged?.Invoke($"{source} 失敗：{error}");
        QuotesChanged?.Invoke();
    }

    private static Uri ExchangeUri(string exchange, string pair) => exchange switch
    {
        "Binance" => new Uri($"wss://stream.binance.com:9443/ws/{pair.Replace("/", string.Empty).ToLowerInvariant()}@ticker"),
        "OKX" => new Uri("wss://ws.okx.com:8443/ws/v5/public"),
        _ => new Uri("wss://stream.bybit.com/v5/public/spot")
    };

    private static string? Subscription(string exchange, string pair) => exchange switch
    {
        "OKX" => JsonSerializer.Serialize(new { op = "subscribe", args = new[] { new { channel = "tickers", instId = pair.Replace('/', '-') } } }),
        "Bybit" => JsonSerializer.Serialize(new { op = "subscribe", args = new[] { $"tickers.{pair.Replace("/", string.Empty)}" } }),
        _ => null
    };

    private static decimal? ReadTicker(string exchange, string json)
    {
        using var document = JsonDocument.Parse(json);
        return exchange switch
        {
            "Binance" when document.RootElement.TryGetProperty("c", out var binance) => MarketPriceParser.Read(binance.GetString()),
            "OKX" when document.RootElement.TryGetProperty("data", out var okx) && okx.GetArrayLength() > 0 => MarketPriceParser.Read(okx[0].GetProperty("last").GetString()),
            "Bybit" when document.RootElement.TryGetProperty("data", out var bybit) && bybit.TryGetProperty("lastPrice", out var last) => MarketPriceParser.Read(last.GetString()),
            _ => null
        };
    }
}

public static class CandleService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<decimal[]> GetAsync(string pair, string timeframe, CancellationToken token)
    {
        var compactPair = pair.Replace("/", string.Empty);
        var okxPair = pair.Replace('/', '-');
        var bybitInterval = timeframe switch { "1h" => "60", "4h" => "240", _ => "15" };
        var requests = new (string Url, Func<string, decimal[]> Read)[]
        {
            ($"https://api.binance.com/api/v3/klines?symbol={compactPair}&interval={timeframe}&limit=200", CandleReader.ReadBinance),
            ($"https://www.okx.com/api/v5/market/candles?instId={okxPair}&bar={timeframe}&limit=200", CandleReader.ReadOkx),
            ($"https://api.bybit.com/v5/market/kline?category=spot&symbol={compactPair}&interval={bybitInterval}&limit=200", CandleReader.ReadBybit)
        };

        foreach (var request in requests)
        {
            try
            {
                var json = await Http.GetStringAsync(request.Url, token);
                var closes = request.Read(json);
                if (closes.Length >= 50)
                {
                    return closes;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
            }
            catch (JsonException)
            {
            }
        }

        throw new InvalidOperationException("所有 K 線來源皆不可用。");
    }
}

public static class AiAnalysisService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<string> GenerateAsync(AppSettings settings, IReadOnlyDictionary<string, AnalysisResult> analyses, CancellationToken token)
    {
        var key = CredentialStore.Read(settings.AiCredentialTarget);
        if (string.IsNullOrWhiteSpace(settings.AiEndpoint) || string.IsNullOrWhiteSpace(settings.AiModel) || string.IsNullOrWhiteSpace(key))
        {
            return "尚未完成 AI 端點、模型或金鑰設定。";
        }

        var summary = string.Join("；", analyses.Select(item => $"{item.Key}：{item.Value.Direction}，上漲機率 {item.Value.UpProbability}% ，RSI {item.Value.Rsi:F1}"));
        var endpoint = settings.AiEndpoint.TrimEnd('/');
        if (!endpoint.EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/v1/chat/completions";
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = settings.AiModel,
            messages = new[]
            {
                new { role = "system", content = "你是加密貨幣市場資訊摘要助手。只解讀提供的技術資料，不提供投資指示。" },
                new { role = "user", content = $"請用繁體中文簡短解讀：{summary}" }
            }
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            return AiResponseReader.ReadContent(await response.Content.ReadAsStringAsync(token)) ?? "AI 未回傳文字內容。";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return $"AI 解讀失敗：{exception.Message}";
        }
    }
}
