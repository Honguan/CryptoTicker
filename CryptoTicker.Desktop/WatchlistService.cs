using System.Collections.Concurrent;
using CryptoTicker.Core;

namespace CryptoTicker.Desktop;

public sealed record WatchPairSnapshot(string Pair, AggregationResult Aggregate, IReadOnlyList<SourceSnapshot> Sources);

public sealed class WatchlistService : IDisposable
{
    private readonly ConcurrentDictionary<string, MarketDataService> _services = new();
    private string[] _pairs = [];

    public event Action? QuotesChanged;

    public async Task StartAsync(IEnumerable<string> pairs)
    {
        Dispose();
        _pairs = pairs.Take(WatchPairList.MaximumPairs).ToArray();
        foreach (var pair in _pairs)
        {
            var service = new MarketDataService();
            service.QuotesChanged += () => QuotesChanged?.Invoke();
            _services[pair] = service;
            await service.StartAsync(new AppSettings { Pair = pair }, includeCustomSources: false);
        }
    }

    public IReadOnlyList<WatchPairSnapshot> Snapshots()
    {
        var snapshots = new List<WatchPairSnapshot>();
        foreach (var pair in _pairs)
        {
            if (_services.TryGetValue(pair, out var service))
            {
                snapshots.Add(new WatchPairSnapshot(pair, service.Aggregate(), service.Sources()));
            }
        }

        return snapshots;
    }

    public void Dispose()
    {
        foreach (var service in _services.Values)
        {
            service.Dispose();
        }

        _services.Clear();
        _pairs = [];
    }
}
