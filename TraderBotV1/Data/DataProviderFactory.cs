using Alpaca.Markets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TraderBotV1.Data
{
	// ═══════════════════════════════════════════════════════════════
	// Market Data Models
	// ═══════════════════════════════════════════════════════════════
	public record MarketBar(
		DateTime TimestampUtc,
		decimal Open,
		decimal High,
		decimal Low,
		decimal Close,
		long Volume
	);

	public interface IMarketDataProvider
	{
		Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory);
	}

	// ═══════════════════════════════════════════════════════════════
	// Data Provider Factory
	// ═══════════════════════════════════════════════════════════════
	public static class DataProviderFactory
	{
		public static IMarketDataProvider Create(Config cfg)
		{
			if (cfg == null)
				throw new ArgumentNullException(nameof(cfg));

			var mode = (cfg.Mode ?? "Auto").ToLowerInvariant();
			var source = (cfg.DataSource ?? "Auto").ToLowerInvariant();

			// Determine which provider to use
			bool usePolygon = source == "polygon" ||
							 (source == "auto" && mode != "live");

			if (usePolygon)
			{
				if (string.IsNullOrWhiteSpace(cfg.Polygon?.ApiKey))
					throw new InvalidOperationException("Polygon API key is required");

				return new PolygonDataProvider(
					cfg.Polygon.ApiKey,
					cfg.Polygon.Timespan ?? "day"
				);
			}

			// Default to Alpaca
			if (string.IsNullOrWhiteSpace(cfg.Alpaca?.ApiKey) ||
				string.IsNullOrWhiteSpace(cfg.Alpaca?.ApiSecret))
				throw new InvalidOperationException("Alpaca API credentials are required");

			return new AlpacaDataProvider(
				cfg.Alpaca.ApiKey,
				cfg.Alpaca.ApiSecret,
				usePaper: cfg.UsePaperWhenLive
			);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Alpaca Data Provider
	// ═══════════════════════════════════════════════════════════════
	public class AlpacaDataProvider : IMarketDataProvider
	{
		private readonly IAlpacaDataClient _dataClient;
		private const decimal MIN_VOLUME_THRESHOLD_RATIO = 0.15m; // 15% of average
		private const long MIN_DAILY_VOLUME = 100000; // Minimum for daily bars
		private const int LOOKBACK_BUFFER_DAYS = 0; // Days to look back from now

		public AlpacaDataProvider(string apiKey, string apiSecret, bool usePaper = true)
		{
			if (string.IsNullOrWhiteSpace(apiKey))
				throw new ArgumentNullException(nameof(apiKey));
			if (string.IsNullOrWhiteSpace(apiSecret))
				throw new ArgumentNullException(nameof(apiSecret));

			var env = usePaper ? Environments.Paper : Environments.Live;
			_dataClient = env.GetAlpacaDataClient(new SecretKey(apiKey, apiSecret));
		}

		public async Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory)
		{
			if (string.IsNullOrWhiteSpace(symbol))
				throw new ArgumentNullException(nameof(symbol));
			if (daysHistory <= 0)
				throw new ArgumentOutOfRangeException(nameof(daysHistory), "Must be positive");

			// Calculate date range (exclude recent days to avoid partial data)
			DateTime endDate = DateTime.Now.AddDays(-LOOKBACK_BUFFER_DAYS);
			var end = endDate.AddHours(-1);// AddDays(-LOOKBACK_BUFFER_DAYS);
			var start = end.AddDays(-daysHistory);

			// Determine timeframe based on history length
			var timeFrame = daysHistory <= 90 ? BarTimeFrame.Hour : BarTimeFrame.Day;

			// Fetch all bars (handle pagination)
			var allBars = await FetchAllBarsAsync(symbol, start, end, timeFrame);

			if (allBars.Count == 0)
			{
				Console.WriteLine($"⚠️ No data returned for {symbol}");
				return new List<MarketBar>();
			}

			// Filter low-volume bars (noise reduction)
			var filteredBars = FilterLowVolumeBars(allBars, timeFrame);

			// Convert to MarketBar format
			return filteredBars
				.Select(b => new MarketBar(
					b.TimeUtc,
					(decimal)b.Open,
					(decimal)b.High,
					(decimal)b.Low,
					(decimal)b.Close,
					(long)b.Volume
				))
				.OrderBy(b => b.TimestampUtc)
				.ToList();
		}

		private async Task<List<IBar>> FetchAllBarsAsync(
			string symbol,
			DateTime start,
			DateTime end,
			BarTimeFrame timeFrame)
		{
			var allBars = new List<IBar>();
			var req = new HistoricalBarsRequest(symbol, start, end, timeFrame) {};
			string? nextPageToken = null;
			int pageCount = 0;
			const int MAX_PAGES = 100; // Safety limit

			do
			{
				req.Pagination.Token = nextPageToken;

				try
				{
					var response = await _dataClient.ListHistoricalBarsAsync(req);
					allBars.AddRange(response.Items);
					nextPageToken = response.NextPageToken;
					pageCount++;

					if (pageCount >= MAX_PAGES)
					{
						Console.WriteLine($"⚠️ Reached max page limit ({MAX_PAGES}) for {symbol}");
						break;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"❌ Error fetching bars for {symbol}: {ex.Message}");
					break;
				}

			} while (!string.IsNullOrEmpty(nextPageToken));

			return allBars;
		}

		private List<IBar> FilterLowVolumeBars(List<IBar> bars, BarTimeFrame timeFrame)
		{
			if (bars.Count == 0) return bars;

			// Calculate average volume
			decimal avgVolume = bars.Average(b => b.Volume);

			// Determine minimum volume threshold
			decimal minVolumeThreshold = timeFrame == BarTimeFrame.Day
				? Math.Max(MIN_DAILY_VOLUME, avgVolume * MIN_VOLUME_THRESHOLD_RATIO)
				: avgVolume * MIN_VOLUME_THRESHOLD_RATIO;

			// Filter out low-volume bars
			var filtered = bars.Where(b => b.Volume >= minVolumeThreshold).ToList();

			int removedCount = bars.Count - filtered.Count;
			if (removedCount > 0)
			{
				Console.WriteLine($"🧹 Filtered {removedCount} low-volume bars (threshold: {minVolumeThreshold:N0})");
			}

			return filtered;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Polygon Data Provider
	// ═══════════════════════════════════════════════════════════════
	public class PolygonDataProvider : IMarketDataProvider
	{
		private readonly PolygonRestClient _client;
		private readonly string _timespan;

		public PolygonDataProvider(string apiKey, string timespan = "day")
		{
			if (string.IsNullOrWhiteSpace(apiKey))
				throw new ArgumentNullException(nameof(apiKey));

			_client = new PolygonRestClient(apiKey);
			_timespan = string.IsNullOrWhiteSpace(timespan) ? "day" : timespan.ToLowerInvariant();

			// Validate timespan
			var validTimespans = new[] { "minute", "hour", "day", "week", "month" };
			if (!validTimespans.Contains(_timespan))
			{
				throw new ArgumentException(
					$"Invalid timespan '{timespan}'. Valid values: {string.Join(", ", validTimespans)}",
					nameof(timespan)
				);
			}
		}

		public async Task<List<MarketBar>> GetBarsAsync(string symbol, int daysHistory)
		{
			if (string.IsNullOrWhiteSpace(symbol))
				throw new ArgumentNullException(nameof(symbol));
			if (daysHistory <= 0)
				throw new ArgumentOutOfRangeException(nameof(daysHistory), "Must be positive");

			try
			{
				var polygonBars = await _client.GetBarsAsync(symbol, _timespan, daysHistory);

				if (polygonBars == null || polygonBars.Count == 0)
				{
					Console.WriteLine($"⚠️ No Polygon data returned for {symbol}");
					return new List<MarketBar>();
				}

				var marketBars = new List<MarketBar>(polygonBars.Count);

				foreach (var bar in polygonBars)
				{
					// Convert Unix timestamp to UTC DateTime
					var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(bar.t).UtcDateTime;

					// Validate bar data
					if (bar.o <= 0 || bar.h <= 0 || bar.l <= 0 || bar.c <= 0)
					{
						Console.WriteLine($"⚠️ Skipping invalid bar at {timestamp}: O={bar.o}, H={bar.h}, L={bar.l}, C={bar.c}");
						continue;
					}

					if (bar.h < bar.l || bar.o > bar.h || bar.o < bar.l || bar.c > bar.h || bar.c < bar.l)
					{
						Console.WriteLine($"⚠️ Skipping inconsistent bar at {timestamp}");
						continue;
					}

					marketBars.Add(new MarketBar(
						timestamp,
						bar.o,
						bar.h,
						bar.l,
						bar.c,
						bar.v
					));
				}

				return marketBars.OrderBy(b => b.TimestampUtc).ToList();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error fetching Polygon data for {symbol}: {ex.Message}");
				return new List<MarketBar>();
			}
		}
	}
}