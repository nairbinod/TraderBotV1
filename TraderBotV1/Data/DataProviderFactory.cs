using Alpaca.Markets;

namespace TraderBotV1.Data
{
    public record MarketBar(
    DateTime TimestampUtc,
    decimal Open, decimal High, decimal Low, decimal Close,
    long Volume
);
    public interface IMarketDataProvider
    {
        Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory);
    }
    public static class DataProviderFactory
    {
        public static IMarketDataProvider Create(Config cfg)
        {
            var mode = (cfg.Mode ?? "Auto").ToLowerInvariant();
            var source = (cfg.DataSource ?? "Auto").ToLowerInvariant();

            bool usePolygon = source == "polygon" ||
                              (source == "auto" && mode != "live");
            bool useAlpaca = source == "alpaca" ||
                              (source == "auto" && mode == "live");

            if (usePolygon)
                return new PolygonDataProvider(cfg.Polygon.ApiKey, cfg.Polygon.Timespan);

            // default to Alpaca for live/auto-live
            return new AlpacaDataProvider(cfg.Alpaca.ApiKey, cfg.Alpaca.ApiSecret, usePaper: cfg.UsePaperWhenLive);
        }
    }

    public class AlpacaDataProvider : IMarketDataProvider
    {
        private readonly IAlpacaDataClient _dataClient;

        public AlpacaDataProvider(string apiKey, string apiSecret, bool usePaper = true)
        {
            var env = usePaper ? Environments.Paper : Environments.Live;
            _dataClient = env.GetAlpacaDataClient(new SecretKey(apiKey, apiSecret));
        }


        public async Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory)
        {
            var end = DateTime.Now.AddHours(-1);
			var start = end.AddDays(-daysHistory);
            
            var timeFrame = daysHistory <= 60 ? BarTimeFrame.Hour : BarTimeFrame.Day;
			var req = new HistoricalBarsRequest(symbol, start, end, timeFrame);

            //var rsp = await _dataClient.ListHistoricalBarsAsync(req);
            var allBars = new List<IBar>();
            string? next = null;
            do
            {
                req.Pagination.Token = next;
                var rsp = await _dataClient.ListHistoricalBarsAsync(req);
                allBars.AddRange(rsp.Items);
                next = rsp.NextPageToken;
            } while (!string.IsNullOrEmpty(next));

			//exclude low volume bars as noise
			decimal avgVol = allBars.Count > 0 ? allBars.Average(b => b.Volume) : 0;
			decimal minVolThreshold = avgVol * 0.2m; //20% of average volume
			if (timeFrame== BarTimeFrame.Day)
            {
                minVolThreshold = 100000;
			}

            return allBars
                .Where(b => b.Volume >= minVolThreshold).Select(b => new MarketBar(b.TimeUtc, (decimal)b.Open, (decimal)b.High, (decimal)b.Low, (decimal)b.Close, (long)b.Volume))
                .ToList();
        }
    }

    public class PolygonDataProvider : IMarketDataProvider
    {
        private readonly PolygonRestClient _client;
        private readonly string _timespan;

        public PolygonDataProvider(string apiKey, string timespan = "day")
        {
            _client = new PolygonRestClient(apiKey);
            _timespan = string.IsNullOrWhiteSpace(timespan) ? "day" : timespan;
        }

        public async Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory)
        {
            var polyBars = await _client.GetBarsAsync(symbol, _timespan, daysHistory);
            var list = new List<MarketBar>(polyBars.Count);
            foreach (var b in polyBars)
            {
                var ts = DateTimeOffset.FromUnixTimeMilliseconds(b.t).UtcDateTime;
                list.Add(new MarketBar(ts, b.o, b.h, b.l, b.c, b.v));
            }
            return list;
        }
    }
}
