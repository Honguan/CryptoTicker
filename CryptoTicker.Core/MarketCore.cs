using System.Globalization;
using System.Text.Json;

namespace CryptoTicker.Core;

public sealed record Quote(string Source, decimal Price, DateTimeOffset UpdatedAt);

public sealed record AggregationResult(decimal? Price, int ActiveSourceCount);

public enum SourceHealth
{
    Fresh,
    Stale,
    Error
}

public sealed record SourceSnapshot(string Source, decimal? LastPrice, DateTimeOffset? LastUpdatedAt, string? LastError, SourceHealth Health);

public sealed class SourceState(string source)
{
    private decimal? _lastPrice;
    private DateTimeOffset? _lastUpdatedAt;
    private string? _lastError;

    public void RecordSuccess(decimal price, DateTimeOffset updatedAt)
    {
        if (price <= 0 || updatedAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        _lastPrice = price;
        _lastUpdatedAt = updatedAt;
        _lastError = null;
    }

    public void RecordFailure(string error) => _lastError = error;

    public SourceSnapshot Snapshot(DateTimeOffset now)
    {
        var health = _lastUpdatedAt is null || now - _lastUpdatedAt > QuoteAggregator.Freshness
            ? SourceHealth.Stale
            : _lastError is not null ? SourceHealth.Error : SourceHealth.Fresh;
        return new SourceSnapshot(source, _lastPrice, _lastUpdatedAt, _lastError, health);
    }
}

public static class QuoteAggregator
{
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);

    public static AggregationResult Aggregate(IEnumerable<Quote> quotes, DateTimeOffset now)
    {
        var prices = quotes
            .Where(quote => quote.Price > 0 && now - quote.UpdatedAt <= Freshness && quote.UpdatedAt <= now)
            .Select(quote => quote.Price)
            .Order()
            .ToArray();

        if (prices.Length < 2)
        {
            return new AggregationResult(null, prices.Length);
        }

        var middle = prices.Length / 2;
        var price = prices.Length % 2 == 0
            ? (prices[middle - 1] + prices[middle]) / 2
            : prices[middle];

        return new AggregationResult(price, prices.Length);
    }
}

public static class MarketPriceParser
{
    public static decimal? Read(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price > 0
        ? price
        : null;
}

public static class JsonPathReader
{
    public static decimal? ReadDecimal(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        var current = Find(document.RootElement, path);
        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(current.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    public static DateTimeOffset? ReadTimestamp(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        var current = Find(document.RootElement, path);
        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var unix))
        {
            return unix > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return current.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(current.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp
            : null;
    }

    private static JsonElement Find(JsonElement root, string path)
    {
        var current = root;

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part;
            var bracket = part.IndexOf('[');
            if (bracket >= 0)
            {
                name = part[..bracket];
            }

            if (!string.IsNullOrEmpty(name) && (!current.TryGetProperty(name, out current)))
            {
                return default;
            }

            if (bracket >= 0)
            {
                var end = part.IndexOf(']', bracket);
                if (end < 0 || !int.TryParse(part[(bracket + 1)..end], out var index) || current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return default;
                }

                current = current[index];
            }
        }

        return current;
    }
}

public enum Direction
{
    Up,
    Down,
    Neutral
}

public enum AlertDirection
{
    Above,
    Below
}

public sealed record PriceAlert(string Pair, AlertDirection Direction, decimal TargetPrice);

public static class AlertEvaluator
{
    public static bool Crossed(PriceAlert alert, decimal previousPrice, decimal currentPrice) => alert.Direction switch
    {
        AlertDirection.Above => previousPrice < alert.TargetPrice && currentPrice >= alert.TargetPrice,
        AlertDirection.Below => previousPrice > alert.TargetPrice && currentPrice <= alert.TargetPrice,
        _ => false
    };
}

public static class SignalTransition
{
    public static bool ShouldNotify(Direction previous, Direction current) =>
        (previous == Direction.Up && current == Direction.Down) ||
        (previous == Direction.Down && current == Direction.Up);
}

public sealed record AnalysisResult(Direction Direction, int UpProbability, decimal Score, decimal Ema20, decimal Ema50, decimal Rsi, decimal Macd);

public static class MarketAnalyzer
{
    public static AnalysisResult Analyze(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 50)
        {
            throw new ArgumentException("至少需要 50 根 K 線。", nameof(closes));
        }

        var ema20 = Ema(closes, 20);
        var ema50 = Ema(closes, 50);
        var rsi = Rsi(closes, 14);
        var (macd, signal) = Macd(closes);
        var score = new[] { Score(ema20, ema50), Score(rsi, 55m, 45m), Score(macd, signal) }.Select(value => (decimal)value).Average();
        var direction = score >= 0.34m ? Direction.Up : score <= -0.34m ? Direction.Down : Direction.Neutral;
        var probability = (int)decimal.Round((score + 1m) * 50m, MidpointRounding.AwayFromZero);

        return new AnalysisResult(direction, probability, score, ema20, ema50, rsi, macd);
    }

    private static decimal Ema(IReadOnlyList<decimal> values, int period)
    {
        var factor = 2m / (period + 1m);
        var result = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            result += (values[index] - result) * factor;
        }

        return result;
    }

    private static decimal Rsi(IReadOnlyList<decimal> values, int period)
    {
        var gains = 0m;
        var losses = 0m;
        var start = Math.Max(1, values.Count - period);
        for (var index = start; index < values.Count; index++)
        {
            var change = values[index] - values[index - 1];
            if (change > 0)
            {
                gains += change;
            }
            else
            {
                losses -= change;
            }
        }

        if (gains == 0m && losses == 0m)
        {
            return 50m;
        }

        return losses == 0m ? 100m : 100m - 100m / (1m + gains / losses);
    }

    private static (decimal Macd, decimal Signal) Macd(IReadOnlyList<decimal> values)
    {
        var ema12 = values[0];
        var ema26 = values[0];
        var signal = 0m;
        for (var index = 1; index < values.Count; index++)
        {
            ema12 += (values[index] - ema12) * 2m / 13m;
            ema26 += (values[index] - ema26) * 2m / 27m;
            var macd = ema12 - ema26;
            signal += (macd - signal) * 2m / 10m;
        }

        return (ema12 - ema26, signal);
    }

    private static int Score(decimal actual, decimal positiveThreshold, decimal negativeThreshold) => actual > positiveThreshold ? 1 : actual < negativeThreshold ? -1 : 0;

    private static int Score(decimal actual, decimal comparison) => actual > comparison ? 1 : actual < comparison ? -1 : 0;
}

public static class CandleReader
{
    public static decimal[] ReadBinance(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateArray()
            .Select(candle => decimal.Parse(candle[4].GetString()!, CultureInfo.InvariantCulture))
            .ToArray();
    }

    public static decimal[] ReadOkx(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(candle => decimal.Parse(candle[4].GetString()!, CultureInfo.InvariantCulture))
            .Reverse()
            .ToArray();
    }

    public static decimal[] ReadBybit(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").GetProperty("list")
            .EnumerateArray()
            .Select(candle => decimal.Parse(candle[4].GetString()!, CultureInfo.InvariantCulture))
            .Reverse()
            .ToArray();
    }
}

public static class AiResponseReader
{
    public static string? ReadContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
               choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }
}
