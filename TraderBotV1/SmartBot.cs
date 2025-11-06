using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TraderBotV1.Data;

namespace TraderBotV1
{
	public class SmartBot
	{
		private readonly IMarketDataProvider _dataProvider;
		private readonly TradeEngineEnhanced _engine;
		private readonly SqliteStorage _db;
		private readonly Config _cfg;

		private const int MIN_BARS_REQUIRED = 60; // Minimum bars needed for analysis
		private const int MAX_CONCURRENT_SYMBOLS = 10; // Limit concurrent processing

		public SmartBot(IMarketDataProvider dataProvider, SqliteStorage db, Config cfg, EmailNotificationService? emailService = null)
		{
			_dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
			_db = db ?? throw new ArgumentNullException(nameof(db));
			_cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
			_engine = new TradeEngineEnhanced(db, cfg.RiskPercent, emailService);
		}

		public async Task RunAsync()
		{
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine("🚀 SmartBot Trading Analysis Starting");
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine($"📅 Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
			Console.WriteLine($"⚙️  Mode: {_cfg.Mode ?? "Auto"}");
			Console.WriteLine($"📊 Data Source: {_cfg.DataSource ?? "Auto"}");
			Console.WriteLine($"📈 History Days: {_cfg.DaysHistory}");
			Console.WriteLine($"💰 Risk Per Trade: {_cfg.RiskPercent:P2}");

			// Get symbols to analyze
			var symbols = GetSymbolsToAnalyze();
			if (symbols.Length == 0)
			{
				Console.WriteLine("\n⚠️ No symbols configured. Please add symbols to config or database.");
				return;
			}

			Console.WriteLine($"🎯 Analyzing {symbols.Length} symbol(s): {string.Join(", ", symbols)}");
			Console.WriteLine("═══════════════════════════════════════════════════════\n");

			// Process symbols with progress tracking
			var results = new Dictionary<string, ProcessResult>();
			int completed = 0;
			int total = symbols.Length;

			foreach (var symbol in symbols)
			{
				completed++;
				Console.WriteLine($"[{completed}/{total}] Processing {symbol}...");

				var result = await ProcessSymbolAsync(symbol);
				results[symbol] = result;

				// Add delay between symbols to respect rate limits
				if (completed < total)
				{
					await Task.Delay(500); // 500ms delay
				}
			}

			await _engine.SendSessionNotificationsAsync("nairbinod@gmail.com");
			// Print summary
			PrintSummary(results);

			UpdateTradedSymbols();
		}

		private void UpdateTradedSymbols()
		{
			var symbols = GetTradedSymbols();
			foreach (var symbol in symbols)
			{
				UpdateCurrentValue(symbol);
			}
		}

		private string[] GetSymbolsToAnalyze()
		{
			// Priority 1: Use symbols from config
			if (_cfg.Symbols != null && _cfg.Symbols.Length > 0)
			{
				return _cfg.Symbols;
			}

			// Priority 2: Get active symbols from database
			try
			{
				var dbSymbols = _db.GetActiveSymbols().ToArray();
				if (dbSymbols.Length > 0)
				{
					Console.WriteLine($"📋 Loaded {dbSymbols.Length} symbols from database");
					return dbSymbols;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"⚠️ Failed to load symbols from database: {ex.Message}");
			}

			// Priority 3: Use default watchlist
			Console.WriteLine("📋 Using default watchlist");
			return new[] { "SPY", "QQQ", "AAPL", "MSFT", "TSLA" };
		}

		private string[] GetTradedSymbols()
		{
			// Priority 1: Use symbols from config
			if (_cfg.Symbols != null && _cfg.Symbols.Length > 0)
			{
				return _cfg.Symbols;
			}

			// Priority 2: Get active symbols from database
			try
			{
				var dbSymbols = _db.GetTradedSymbols().ToArray();
				if (dbSymbols.Length > 0)
				{
					Console.WriteLine($"📋 Loaded Traded {dbSymbols.Length} symbols from database");
					return dbSymbols;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"⚠️ Failed to load symbols from database: {ex.Message}");
			}

			// Priority 3: Use default watchlist
			Console.WriteLine("📋 Using default watchlist");
			return new[] { "SPY", "QQQ", "AAPL", "MSFT", "TSLA" };
		}

		private async Task<decimal> FetchCurrentPrice(string symbol)
		{
			try
			{
				// Fetch market data
				Console.WriteLine($"   📡 Fetching market data for {symbol}...");
				var bars = await _dataProvider.GetBarsAsync(symbol, 1);
				return bars.Last().Close;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ❌ Error processing {symbol}: {ex.Message}");
			}
			return 0;
		}
		private void UpdateCurrentValue(string symbol)
		{
			try
			{
				var currentPrice = FetchCurrentPrice(symbol).Result;
				_db.UpdateCurrentValue(symbol, currentPrice);
				Console.WriteLine($"   💾 Updated current price for {symbol} to {currentPrice}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ❌ Error updating current price for {symbol}: {ex.Message}");
			}
		}
		private async Task<ProcessResult> ProcessSymbolAsync(string symbol)
		{
			var result = new ProcessResult { Symbol = symbol };

			try
			{
				// Validate symbol
				if (string.IsNullOrWhiteSpace(symbol))
				{
					result.Status = "Skipped";
					result.Message = "Invalid symbol";
					return result;
				}

				// Fetch market data
				Console.WriteLine($"   📡 Fetching market data for {symbol}...");
				var bars = await _dataProvider.GetBarsAsync(symbol, _cfg.DaysHistory);

				// Validate data availability
				if (bars == null || bars.Count == 0)
				{
					result.Status = "Failed";
					result.Message = "No data returned";
					Console.WriteLine($"   ❌ No data available for {symbol}");
					return result;
				}

				if (bars.Count < MIN_BARS_REQUIRED)
				{
					result.Status = "Skipped";
					result.Message = $"Insufficient data ({bars.Count} bars, need {MIN_BARS_REQUIRED})";
					Console.WriteLine($"   ⚠️ {result.Message}");
					return result;
				}

				// Validate data quality
				var dataQuality = ValidateDataQuality(bars);
				if (!dataQuality.IsValid)
				{
					result.Status = "Failed";
					result.Message = dataQuality.Message;
					Console.WriteLine($"   ❌ Data quality issue: {dataQuality.Message}");
					return result;
				}

				// Extract price data
				var opens = bars.Select(b => b.Open).ToList();
				var closes = bars.Select(b => b.Close).ToList();
				var highs = bars.Select(b => b.High).ToList();
				var lows = bars.Select(b => b.Low).ToList();
				var volumes = bars.Select(b => (decimal)b.Volume).ToList();

				var lastBarDate = bars.Max(b => b.TimestampUtc);

				// Optional: Persist price data to database
				if (_cfg.PersistPriceData)
				{
					PersistPriceData(symbol, bars);
				}

				// Run trading analysis
				Console.WriteLine($"   🔬 Analyzing {symbol} ({bars.Count} bars)...");
				_engine.EvaluateAndLog(symbol, closes, highs, lows, volumes , opens, lastBarDate);

				result.Status = "Success";
				result.Message = $"Analyzed {bars.Count} bars";
				result.BarCount = bars.Count;

				Console.WriteLine($"   ✅ Completed analysis for {symbol}");
			}
			catch (Exception ex)
			{
				result.Status = "Error";
				result.Message = ex.Message;
				Console.WriteLine($"   ❌ Error processing {symbol}: {ex.Message}");

				// Log error to database
				try
				{
					_db.InsertSignal(symbol, DateTime.UtcNow, "Error", "Hold", ex.Message);
				}
				catch
				{
					// Ignore database errors during error handling
				}
			}

			return result;
		}

		private DataQualityResult ValidateDataQuality(List<MarketBar> bars)
		{
			// Check for gaps in data
			var timestamps = bars.Select(b => b.TimestampUtc).OrderBy(t => t).ToList();
			for (int i = 1; i < timestamps.Count; i++)
			{
				var gap = (timestamps[i] - timestamps[i - 1]).TotalDays;
				if (gap > 7) // More than 7 days gap
				{
					return new DataQualityResult
					{
						IsValid = false,
						Message = $"Large data gap detected: {gap:F1} days between {timestamps[i - 1]:yyyy-MM-dd} and {timestamps[i]:yyyy-MM-dd}"
					};
				}
			}

			// Check for invalid prices
			var invalidBars = bars.Where(b =>
				b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0 ||
				b.High < b.Low ||
				b.Open > b.High || b.Open < b.Low ||
				b.Close > b.High || b.Close < b.Low
			).ToList();

			if (invalidBars.Any())
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = $"{invalidBars.Count} bar(s) with invalid price data"
				};
			}

			// Check for suspiciously low volume
			var avgVolume = bars.Average(b => b.Volume);
			if (avgVolume < 1000) // Very low average volume
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = $"Suspiciously low average volume: {avgVolume:N0}"
				};
			}

			// Check for price consistency (no extreme outliers)
			var prices = bars.Select(b => b.Close).ToList();
			var avgPrice = prices.Average();
			var maxPrice = prices.Max();
			var minPrice = prices.Min();

			if (maxPrice > avgPrice * 10 || minPrice < avgPrice * 0.1m)
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = "Extreme price outliers detected (possible data error)"
				};
			}

			return new DataQualityResult { IsValid = true, Message = "Data quality OK" };
		}

		private void PersistPriceData(string symbol, List<MarketBar> bars)
		{
			try
			{
				int savedCount = 0;
				foreach (var bar in bars)
				{
					_db.InsertPrice(
						symbol,
						bar.TimestampUtc,
						bar.Open,
						bar.High,
						bar.Low,
						bar.Close,
						bar.Volume
					);
					savedCount++;
				}
				Console.WriteLine($"   💾 Saved {savedCount} price bars to database");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ⚠️ Failed to persist price data: {ex.Message}");
			}
		}

		private void PrintSummary(Dictionary<string, ProcessResult> results)
		{
			Console.WriteLine("\n═══════════════════════════════════════════════════════");
			Console.WriteLine("📊 ANALYSIS SUMMARY");
			Console.WriteLine("═══════════════════════════════════════════════════════");

			var successful = results.Values.Count(r => r.Status == "Success");
			var failed = results.Values.Count(r => r.Status == "Failed");
			var skipped = results.Values.Count(r => r.Status == "Skipped");
			var errors = results.Values.Count(r => r.Status == "Error");

			Console.WriteLine($"✅ Successful: {successful}");
			Console.WriteLine($"❌ Failed:     {failed}");
			Console.WriteLine($"⚠️  Skipped:    {skipped}");
			Console.WriteLine($"🔥 Errors:     {errors}");

			if (failed > 0 || skipped > 0 || errors > 0)
			{
				Console.WriteLine("\n📝 Details:");
				foreach (var (symbol, result) in results.Where(r => r.Value.Status != "Success"))
				{
					Console.WriteLine($"   {symbol}: {result.Status} - {result.Message}");
				}
			}

			if (successful > 0)
			{
				var totalBars = results.Values.Where(r => r.Status == "Success").Sum(r => r.BarCount);
				Console.WriteLine($"\n📈 Total bars analyzed: {totalBars:N0}");
			}

			Console.WriteLine("\n💾 All results have been saved to the database");
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine($"✅ Analysis completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
			Console.WriteLine("═══════════════════════════════════════════════════════\n");
		}

		// Helper classes
		private class ProcessResult
		{
			public string Symbol { get; set; } = "";
			public string Status { get; set; } = "Unknown";
			public string Message { get; set; } = "";
			public int BarCount { get; set; } = 0;
		}

		private class DataQualityResult
		{
			public bool IsValid { get; set; }
			public string Message { get; set; } = "";
		}
	}
}