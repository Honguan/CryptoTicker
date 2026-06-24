using System.Reflection;

var assembly = Assembly.Load("CryptoTicker.Core");
var failures = 0;

Run("整合價格取新鮮來源中位數", () =>
{
    var quote = RequiredType("CryptoTicker.Core.Quote");
    var aggregator = RequiredType("CryptoTicker.Core.QuoteAggregator");
    var now = DateTimeOffset.UtcNow;
    var quotes = Array.CreateInstance(quote, 3);
    quotes.SetValue(Activator.CreateInstance(quote, "Binance", 100m, now)!, 0);
    quotes.SetValue(Activator.CreateInstance(quote, "OKX", 110m, now)!, 1);
    quotes.SetValue(Activator.CreateInstance(quote, "Bybit", 999m, now.AddSeconds(-16))!, 2);

    var result = aggregator.GetMethod("Aggregate")!.Invoke(null, [quotes, now]);
    Equal(105m, (decimal)result!.GetType().GetProperty("Price")!.GetValue(result)!);
});

Run("來源不足時不產生整合價格", () =>
{
    var quote = RequiredType("CryptoTicker.Core.Quote");
    var aggregator = RequiredType("CryptoTicker.Core.QuoteAggregator");
    var now = DateTimeOffset.UtcNow;
    var quotes = Array.CreateInstance(quote, 2);
    quotes.SetValue(Activator.CreateInstance(quote, "Binance", 100m, now)!, 0);
    quotes.SetValue(Activator.CreateInstance(quote, "OKX", 110m, now.AddSeconds(-16))!, 1);

    var result = aggregator.GetMethod("Aggregate")!.Invoke(null, [quotes, now]);
    Equal(null, result!.GetType().GetProperty("Price")!.GetValue(result));
});

Run("JSON 路徑可讀取數值字串", () =>
{
    var reader = RequiredType("CryptoTicker.Core.JsonPathReader");
    var value = reader.GetMethod("ReadDecimal")!.Invoke(null, ["{\"data\":{\"last\":\"123.45\"}}", "data.last"]);
    Equal(123.45m, value);
});

Run("JSON 路徑可讀取 Unix 時間", () =>
{
    var reader = RequiredType("CryptoTicker.Core.JsonPathReader");
    var value = reader.GetMethod("ReadTimestamp")!.Invoke(null, ["{\"data\":{\"updated\":1700000000}}", "data.updated"]);
    Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), value);
});

Run("規則分析可區分上漲下跌與中性", () =>
{
    var analyzer = RequiredType("CryptoTicker.Core.MarketAnalyzer");
    var method = analyzer.GetMethod("Analyze")!;

    Equal("Up", method.Invoke(null, [Enumerable.Range(1, 200).Select(x => (decimal)x).ToArray()])!.GetType().GetProperty("Direction")!.GetValue(method.Invoke(null, [Enumerable.Range(1, 200).Select(x => (decimal)x).ToArray()]))!.ToString());
    Equal("Down", method.Invoke(null, [Enumerable.Range(1, 200).Reverse().Select(x => (decimal)x).ToArray()])!.GetType().GetProperty("Direction")!.GetValue(method.Invoke(null, [Enumerable.Range(1, 200).Reverse().Select(x => (decimal)x).ToArray()]))!.ToString());
    Equal("Neutral", method.Invoke(null, [Enumerable.Repeat(100m, 200).ToArray()])!.GetType().GetProperty("Direction")!.GetValue(method.Invoke(null, [Enumerable.Repeat(100m, 200).ToArray()]))!.ToString());
});

Run("可解析 Binance K 線與 AI 回應", () =>
{
    var candleReader = RequiredType("CryptoTicker.Core.CandleReader");
    var closes = (decimal[])candleReader.GetMethod("ReadBinance")!.Invoke(null, ["[[0,\"0\",\"0\",\"0\",\"10.5\"],[0,\"0\",\"0\",\"0\",\"11.5\"]]"])!;
    Equal(11.5m, closes[^1]);

    var responseReader = RequiredType("CryptoTicker.Core.AiResponseReader");
    var content = responseReader.GetMethod("ReadContent")!.Invoke(null, ["{\"choices\":[{\"message\":{\"content\":\"市場偏多\"}}]}"]);
    Equal("市場偏多", content);
});

return failures == 0 ? 0 : 1;

Type RequiredType(string name) => assembly.GetType(name) ?? throw new InvalidOperationException($"缺少型別：{name}");

void Run(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"通過：{name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"失敗：{name}：{exception.InnerException?.Message ?? exception.Message}");
    }
}

void Equal(object? expected, object? actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"預期 {expected ?? "null"}，實際 {actual ?? "null"}");
    }
}
