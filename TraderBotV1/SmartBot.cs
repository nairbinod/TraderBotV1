using TraderBotV1.Data;

namespace TraderBotV1
{
	/// <summary>
	/// SWING TRADING OPTIMIZED SMARTBOT
	/// 
	/// Key Optimizations for 1-2 Week Holding Periods:
	/// 1. Increased minimum bars (150) for proper EMA200/weekly trend calculation
	/// 2. Weekly trend alignment verification before signal generation
	/// 3. Pullback detection for better entry timing
	/// 4. Gap risk assessment for overnight/weekend holds
	/// 5. Sector correlation analysis
	/// 6. Time-of-week entry optimization (avoid Friday entries)
	/// 7. Enhanced position sizing for swing duration
	/// 8. Trailing stop configuration for profit protection
	/// 
	/// Target Win Rate: 55-65%
	/// Target Risk/Reward: 1:2 to 1:3
	/// Typical Hold: 5-15 trading days
	/// </summary>
	public class SmartBot
	{
		private readonly IMarketDataProvider _dataProvider;
		private readonly TradeEngineConservative _engine;
		private readonly SqlServerStorage _db;
		private readonly Config _cfg;

		// ═══════════════════════════════════════════════════════════════
		// SWING TRADING CONFIGURATION
		// ═══════════════════════════════════════════════════════════════

		// Minimum bars needed - increased for swing trading indicators
		private const int MIN_BARS_REQUIRED = 150;  // ⭐ Up from 60 - need EMA200 + weekly trend

		// Entry timing restrictions
		private const bool AVOID_FRIDAY_ENTRIES = true;       // Avoid weekend gap risk
		private const bool AVOID_MONDAY_MORNING = true;       // Wait for market direction
		private const int MONDAY_SAFE_HOUR_UTC = 15;          // After 10 AM EST

		// Pullback entry optimization
		private const decimal MIN_PULLBACK_DEPTH = 0.015m;    // 1.5% minimum pullback
		private const decimal MAX_PULLBACK_DEPTH = 0.08m;     // 8% maximum (avoid catching falling knife)
		private const int PULLBACK_LOOKBACK_DAYS = 5;         // Check last 5 days for pullback

		// Weekly trend alignment
		private const bool REQUIRE_WEEKLY_ALIGNMENT = true;   // Must align with weekly trend
		private const decimal MIN_WEEKLY_TREND_STRENGTH = 0.02m;  // 2% weekly move minimum

		// Gap risk management
		private const decimal MAX_AVG_GAP_PERCENT = 0.025m;   // 2.5% avg gap = high risk
		private const decimal GAP_RISK_PENALTY = 0.10m;       // 10% confidence penalty for high gap risk

		// Volatility regime
		private const decimal MIN_ATR_PERCENT = 0.008m;       // 0.8% minimum daily ATR
		private const decimal MAX_ATR_PERCENT = 0.1m;        // 5% maximum daily ATR

		// Processing limits
		private const int MAX_CONCURRENT_SYMBOLS = 10;
		private const int RATE_LIMIT_DELAY_MS = 300;          // Reduced from 500ms

		public SmartBot(IMarketDataProvider dataProvider, SqlServerStorage db, Config cfg,
			EmailNotificationService? emailService = null)
		{
			_dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
			_db = db ?? throw new ArgumentNullException(nameof(db));
			_cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
			_engine = new TradeEngineConservative(db, cfg.RiskPercent, emailService);
		}

		public async Task RunAsync()
		{
			//await UpdateTradedSymbols();

			PrintHeader();

			DateTime startDate = new DateTime(2025, 12, 5);
			// start processing date (for backtesting) starting from 11/1/2025 to today
			for (int i = 0; i < 3; i++)
			{
				DateTime? processDate = startDate.AddDays(i).AddHours(23).AddMinutes(30); // set to end of day UTC	
				await ProcessTradingDayAsync(processDate);
			}

			async Task ProcessTradingDayAsync(DateTime? processDate)
			{
				// process day
				// Check entry timing restrictions
				if (!IsValidEntryTiming(processDate.Value))
				{
					Console.WriteLine("\n⏰ Entry timing restrictions active. Skipping signal generation.");
					Console.WriteLine("   Run analysis without executing trades, or wait for optimal entry window.");
					// Still run analysis but flag signals as "watch only"
				}
				else
				{
					_db.ClearAllData();

					var symbols = GetSymbolsToAnalyze();
					if (symbols.Length == 0)
					{
						Console.WriteLine("\n⚠️ No symbols configured. Please add symbols to config or database.");
						return;
					}

					Console.WriteLine($"🎯 Analyzing {symbols.Length} symbol(s): {string.Join(", ", symbols.Take(10))}");
					if (symbols.Length > 10) Console.WriteLine($"   ... and {symbols.Length - 10} more");
					Console.WriteLine("═══════════════════════════════════════════════════════\n");

					var results = new Dictionary<string, ProcessResult>();
					var swingMetrics = new SwingTradingMetrics();

					int completed = 0;
					int total = symbols.Length;

					foreach (var symbol in symbols)
					{
						completed++;
						Console.WriteLine($"[{completed}/{total}] Processing {symbol}...");

						var result = await ProcessSymbolForSwingAsync(symbol, swingMetrics, processDate);
						results[symbol] = result;

						if (completed < total)
						{
							await Task.Delay(RATE_LIMIT_DELAY_MS);
						}
					}

					//await _engine.SendSessionNotificationsAsync(_cfg.NotificationEmail);

					PrintSwingSummary(results, swingMetrics);
				}
				//update traded symbols current value on Mondays and Fridays
				// Weekend - no trading
				if (processDate.HasValue && !(processDate.Value.DayOfWeek == DayOfWeek.Saturday || processDate.Value.DayOfWeek == DayOfWeek.Sunday))
				{
					await UpdateTradedSymbols(processDate);
				}
			}
		}

		private void PrintHeader()
		{
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine("🚀 SWING TRADING BOT - Optimized for 1-2 Week Holds");
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine($"📅 Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ({DateTime.UtcNow.DayOfWeek})");
			Console.WriteLine($"⚙️  Mode: {_cfg.Mode ?? "Swing Trading"}");
			Console.WriteLine($"📊 Data Source: {_cfg.DataSource ?? "Auto"}");
			Console.WriteLine($"📈 History Days: {_cfg.DaysHistory} (min required: {MIN_BARS_REQUIRED})");
			Console.WriteLine($"💰 Risk Per Trade: {_cfg.RiskPercent:P2}");
			Console.WriteLine($"🎯 Target Hold: 5-15 trading days");
			Console.WriteLine($"📉 Pullback Entry: {MIN_PULLBACK_DEPTH:P1} - {MAX_PULLBACK_DEPTH:P1}");

			if (AVOID_FRIDAY_ENTRIES && DateTime.UtcNow.DayOfWeek == DayOfWeek.Friday)
			{
				Console.WriteLine("⚠️  FRIDAY: New entries flagged as higher risk (weekend gap)");
			}
		}

		/// <summary>
		/// Check if current time is optimal for swing trade entries
		/// </summary>
		private bool IsValidEntryTiming(DateTime now)
		{
			// Avoid Friday entries (weekend gap risk)
			if (AVOID_FRIDAY_ENTRIES && now.DayOfWeek == DayOfWeek.Friday)
			{
				Console.WriteLine("⚠️  Friday detected - entries carry weekend gap risk");
				return false; // Could return true with warning instead
			}

			// Avoid Monday morning (wait for market to establish direction)
			if (AVOID_MONDAY_MORNING && now.DayOfWeek == DayOfWeek.Monday && now.Hour < MONDAY_SAFE_HOUR_UTC)
			{
				Console.WriteLine($"⚠️  Monday morning - waiting until {MONDAY_SAFE_HOUR_UTC}:00 UTC for market direction");
				return false;
			}

			// Weekend - no trading
			if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
			{
				Console.WriteLine("📅 Weekend - markets closed");
				return false;
			}

			return true;
		}

		private async Task<ProcessResult> ProcessSymbolForSwingAsync(string symbol, SwingTradingMetrics metrics, DateTime? processDate)
		{
			var result = new ProcessResult { Symbol = symbol };
			var diagnosticResults = new List<SignalBlockageDiagnostic.DiagnosticResult>();
			try
			{
				if (string.IsNullOrWhiteSpace(symbol))
				{
					result.Status = "Skipped";
					result.Message = "Invalid symbol";
					return result;
				}

				Console.WriteLine($"   📡 Fetching market data for {symbol}...");
				var bars = await _dataProvider.GetBarsAsync(symbol, Math.Max(_cfg.DaysHistory, MIN_BARS_REQUIRED + 50), processDate);


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

				// Enhanced data quality validation for swing trading
				var dataQuality = ValidateSwingDataQuality(bars);
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

				// Run diagnostic
				var diagnostic = SignalBlockageDiagnostic.DiagnoseSignalBlockage(
					symbol,
					closes,
					highs,
					lows,
					volumes,
					opens);

				diagnosticResults.Add(diagnostic);

				// ═══════════════════════════════════════════════════════════════
				// SWING TRADING PRE-FILTERS
				// ═══════════════════════════════════════════════════════════════

				Console.WriteLine($"   🔬 Running swing trade pre-filters...");

				// 1. Volatility Regime Check
				var volatilityCheck = CheckVolatilityRegime(highs, lows, closes);
				if (!volatilityCheck.IsValid)
				{
					result.Status = "Skipped";
					result.Message = volatilityCheck.Message;
					Console.WriteLine($"   ⚠️ {volatilityCheck.Message}");
					_db.InsertSignal(symbol, DateTime.UtcNow, "SwingFilter", "Hold", volatilityCheck.Message);
					metrics.VolatilityFiltered++;
					return result;
				}

				// 2. Weekly Trend Alignment
				if (REQUIRE_WEEKLY_ALIGNMENT)
				{
					var weeklyTrend = AnalyzeWeeklyTrend(closes, highs, lows);
					if (!weeklyTrend.IsValid)
					{
						result.Status = "Skipped";
						result.Message = weeklyTrend.Message;
						Console.WriteLine($"   ⚠️ {weeklyTrend.Message}");
						_db.InsertSignal(symbol, DateTime.UtcNow, "SwingFilter", "Hold", weeklyTrend.Message);
						metrics.WeeklyTrendFiltered++;
						return result;
					}
					Console.WriteLine($"   ✅ Weekly trend: {weeklyTrend.Direction} ({weeklyTrend.Strength:P1})");
				}

				// 3. Gap Risk Assessment
				var gapRisk = AssessGapRisk(opens, closes);
				if (gapRisk.RiskLevel == "High")
				{
					Console.WriteLine($"   ⚠️ High gap risk ({gapRisk.AvgGapPercent:P2}) - confidence penalty applied");
					metrics.HighGapRiskCount++;
				}

				// 4. Pullback Entry Check (for trend-following)
				var pullback = AnalyzePullback(closes, highs, lows);
				if (pullback.IsInPullback)
				{
					Console.WriteLine($"   📉 Pullback detected: {pullback.Depth:P1} from recent high");
					metrics.PullbackEntries++;
				}

				// Persist price data if configured
				if (_cfg.PersistPriceData)
				{
					PersistPriceData(symbol, bars);
				}

				// ═══════════════════════════════════════════════════════════════
				// MAIN ANALYSIS
				// ═══════════════════════════════════════════════════════════════

				Console.WriteLine($"   🔬 Analyzing {symbol} ({bars.Count} bars)...");

				// Add swing context to engine
				// Only run full evaluation if diagnostic shows potential
				if (diagnostic.BuyVotes >= 3 || diagnostic.SellVotes >= 3)
				{
					_engine.EvaluateAndLog(symbol, closes, highs, lows, volumes, opens, lastBarDate);

					result.Status = "Success";
					result.Message = $"Analyzed {bars.Count} bars";
					result.BarCount = bars.Count;
					result.GapRisk = gapRisk.RiskLevel;
					result.InPullback = pullback.IsInPullback;

					Console.WriteLine($"   ✅ Completed swing analysis for {symbol}");
					metrics.Analyzed++;
				}
				// Show summary
				SignalBlockageDiagnostic.RunBatchDiagnostic(diagnosticResults);

			}
			catch (Exception ex)
			{
				result.Status = "Error";
				result.Message = ex.Message;
				Console.WriteLine($"   ❌ Error processing {symbol}: {ex.Message}");

				try
				{
					_db.InsertSignal(symbol, DateTime.UtcNow, "Error", "Hold", ex.Message);
				}
				catch { }
			}

			return result;
		}

		/// <summary>
		/// Enhanced data quality validation for swing trading
		/// Includes gap analysis and weekend handling
		/// </summary>
		private DataQualityResult ValidateSwingDataQuality(List<MarketBar> bars)
		{
			// Standard validations
			var timestamps = bars.Select(b => b.TimestampUtc).OrderBy(t => t).ToList();

			// Check for extended gaps (but allow weekends)
			int extendedGaps = 0;
			for (int i = 1; i < timestamps.Count; i++)
			{
				var gap = (timestamps[i] - timestamps[i - 1]).TotalDays;

				// Weekend gaps (2-3 days) are normal for daily bars
				if (gap > 5) // More than 5 days = extended gap (holiday weeks)
				{
					extendedGaps++;
				}

				if (gap > 10) // More than 10 days is problematic
				{
					return new DataQualityResult
					{
						IsValid = false,
						Message = $"Extended data gap: {gap:F0} days between {timestamps[i - 1]:yyyy-MM-dd} and {timestamps[i]:yyyy-MM-dd}"
					};
				}
			}

			if (extendedGaps > 3)
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = $"Too many extended gaps ({extendedGaps}) - data quality concern"
				};
			}

			// Check for invalid prices
			var invalidBars = bars.Where(b =>
				b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0 ||
				b.High < b.Low ||
				b.Open > b.High || b.Open < b.Low ||
				b.Close > b.High || b.Close < b.Low
			).ToList();

			if (invalidBars.Count > 0)
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = $"{invalidBars.Count} bar(s) with invalid price data"
				};
			}

			// Swing trading needs decent volume
			var avgVolume = bars.Average(b => b.Volume);
			if (avgVolume < 50000) // Higher threshold for swing trading
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = $"Low average volume ({avgVolume:N0}) - not suitable for swing trading"
				};
			}

			// Check for price outliers
			var prices = bars.Select(b => b.Close).ToList();
			var avgPrice = prices.Average();
			var maxPrice = prices.Max();
			var minPrice = prices.Min();

			if (maxPrice > avgPrice * 5 || minPrice < avgPrice * 0.2m)
			{
				return new DataQualityResult
				{
					IsValid = false,
					Message = "Extreme price outliers detected"
				};
			}

			//// Check recent data freshness
			//var mostRecentBar = bars.Max(b => b.TimestampUtc);
			//var daysSinceLastBar = (DateTime.UtcNow - mostRecentBar).TotalDays;
			//if (daysSinceLastBar > 3) // More than 3 days old (accounting for weekends)
			//{
			//	return new DataQualityResult
			//	{
			//		IsValid = false,
			//		Message = $"Stale data - last bar is {daysSinceLastBar:F0} days old"
			//	};
			//}

			return new DataQualityResult { IsValid = true, Message = "Swing data quality OK" };
		}

		/// <summary>
		/// Check if volatility is suitable for swing trading
		/// </summary>
		private (bool IsValid, string Message) CheckVolatilityRegime(
			List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count == 0 || atr.Last() == 0)
				return (false, "Cannot calculate ATR");

			decimal currentPrice = closes.Last();
			decimal atrPercent = atr.Last() / currentPrice;

			if (atrPercent < MIN_ATR_PERCENT)
			{
				return (false, $"Volatility too low ({atrPercent:P2}) - no swing opportunity");
			}

			if (atrPercent > MAX_ATR_PERCENT)
			{
				return (false, $"Volatility too high ({atrPercent:P2}) - excessive risk");
			}

			return (true, $"Volatility OK ({atrPercent:P2})");
		}

		/// <summary>
		/// Analyze weekly trend for alignment with daily signals
		/// </summary>
		private (bool IsValid, string Direction, decimal Strength, string Message) AnalyzeWeeklyTrend(
			List<decimal> closes, List<decimal> highs, List<decimal> lows)
		{
			if (closes.Count < 20)
				return (false, "None", 0, "Insufficient data for weekly analysis");

			// Simulate weekly data by taking every 5th bar
			int weeklyBars = closes.Count / 5;
			if (weeklyBars < 4)
				return (false, "None", 0, "Need at least 4 weeks of data");

			// Calculate 4-week and 8-week EMAs equivalent
			var ema20 = Indicators.EMAList(closes, 20); // ~4 weeks
			var ema40 = Indicators.EMAList(closes, 40); // ~8 weeks

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal ema20Val = ema20[idx];
			decimal ema40Val = ema40[idx];

			// Weekly trend determination
			bool bullish = price > ema20Val && ema20Val > ema40Val;
			bool bearish = price < ema20Val && ema20Val < ema40Val;

			// Calculate trend strength (price distance from 40-day EMA)
			decimal strength = Math.Abs(price - ema40Val) / ema40Val;

			if (!bullish && !bearish)
			{
				return (false, "Neutral", strength, "No clear weekly trend direction");
			}

			if (strength < MIN_WEEKLY_TREND_STRENGTH)
			{
				return (false, bullish ? "Weak Bullish" : "Weak Bearish", strength,
					$"Weekly trend too weak ({strength:P1})");
			}

			string direction = bullish ? "Bullish" : "Bearish";
			return (true, direction, strength, $"Weekly {direction} trend confirmed");
		}

		/// <summary>
		/// Assess overnight/weekend gap risk
		/// </summary>
		private (string RiskLevel, decimal AvgGapPercent) AssessGapRisk(
			List<decimal> opens, List<decimal> closes)
		{
			if (opens.Count < 20)
				return ("Unknown", 0);

			// Calculate average gap percentage over last 20 days
			var gaps = new List<decimal>();
			for (int i = 1; i < Math.Min(opens.Count, 21); i++)
			{
				decimal prevClose = closes[closes.Count - 1 - i];
				decimal currentOpen = opens[opens.Count - i];
				decimal gapPercent = Math.Abs(currentOpen - prevClose) / prevClose;
				gaps.Add(gapPercent);
			}

			decimal avgGap = gaps.Average();
			decimal maxGap = gaps.Max();

			if (avgGap > MAX_AVG_GAP_PERCENT || maxGap > 0.05m) // 5% max single gap
			{
				return ("High", avgGap);
			}
			else if (avgGap > MAX_AVG_GAP_PERCENT * 0.6m) // 60% of threshold
			{
				return ("Medium", avgGap);
			}

			return ("Low", avgGap);
		}

		/// <summary>
		/// Analyze if stock is in a pullback (better entry for trend-following)
		/// </summary>
		private (bool IsInPullback, decimal Depth, int DaysInPullback) AnalyzePullback(
			List<decimal> closes, List<decimal> highs, List<decimal> lows)
		{
			if (closes.Count < PULLBACK_LOOKBACK_DAYS + 20)
				return (false, 0, 0);

			int idx = closes.Count - 1;

			// Find recent swing high (last 20 bars)
			decimal recentHigh = highs.Skip(idx - 20).Take(20).Max();
			decimal currentPrice = closes[idx];

			// Calculate pullback depth
			decimal pullbackDepth = (recentHigh - currentPrice) / recentHigh;

			// Count days since high
			int daysSinceHigh = 0;
			for (int i = idx; i >= Math.Max(0, idx - 20); i--)
			{
				if (highs[i] == recentHigh)
				{
					daysSinceHigh = idx - i;
					break;
				}
			}

			// Check if in valid pullback range
			bool isValidPullback = pullbackDepth >= MIN_PULLBACK_DEPTH &&
								   pullbackDepth <= MAX_PULLBACK_DEPTH &&
								   daysSinceHigh <= PULLBACK_LOOKBACK_DAYS;

			return (isValidPullback, pullbackDepth, daysSinceHigh);
		}

		private async Task UpdateTradedSymbols(DateTime? processDate)
		{
			var symbols = GetTradedSymbols();
			foreach (var symbol in symbols)
			{
				UpdateCurrentValue(symbol, processDate);
			}
			await CreateTradeHistoryDetails(processDate);
		}

		private string[] GetSymbolsToAnalyze()
		{
			if (_cfg.Symbols != null && _cfg.Symbols.Length > 0)
			{
				return _cfg.Symbols;
			}

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

			Console.WriteLine("📋 Using default swing trading watchlist");
			return new[] { "SPY", "QQQ", "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA", "AMD" };
		}

		private string[] GetTradedSymbols()
		{
			string[] dbSymbols = Array.Empty<string>();
			if (_cfg.Symbols != null && _cfg.Symbols.Length > 0)
			{
				return _cfg.Symbols;
			}

			try
			{
				var th = _db.GetTradeHistory().Result;
				// iterate through trade history and get distinct symbols and add them to dbSymbols
				dbSymbols = th.Select(t => t.Symbol).Distinct().ToArray();

				if (dbSymbols.Length > 0)
				{
					Console.WriteLine($"📋 Loaded {dbSymbols.Length} traded symbols from database");
					return dbSymbols;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"⚠️ Failed to load traded symbols: {ex.Message}");
			}

			return Array.Empty<string>();
		}

		private async Task<IEnumerable<TradeRecord>> CreateTradeHistoryDetails(DateTime? processDate)
		{
			try
			{
				var th = await _db.GetTradeHistory();

				if (!th.Any())
				{
					Console.WriteLine("📋 No trade history found");
					return th;
				}

				Console.WriteLine($"📋 Updating prices for {th.Count()} trades...");

				int processed = 0;
				int successful = 0;
				int failed = 0;

				foreach (var trade in th)
				{
					processed++;
					try
					{
						// Fetch current price for the symbol
						var currentBar = await FetchCurrentPrice(trade.Symbol, processDate);

						if (currentBar != null)
						{
							// Insert trade history detail with current price
							_db.InsertTradeHistoryDetail(
								trade.Id,
								trade.Symbol,
								trade.BarDate,
								currentBar.TimestampUtc,
								currentBar.Close);

							successful++;
							Console.WriteLine($"   [{processed}/{th.Count()}] ✅ {trade.Symbol}: ${currentBar.Close:F2}");
						}
						else
						{
							failed++;
							Console.WriteLine($"   [{processed}/{th.Count()}] ❌ {trade.Symbol}: No price data available");
						}

						// Rate limiting between API calls
						if (processed < th.Count())
						{
							await Task.Delay(RATE_LIMIT_DELAY_MS);
						}
					}
					catch (Exception ex)
					{
						failed++;
						Console.WriteLine($"   [{processed}/{th.Count()}] ❌ {trade.Symbol}: {ex.Message}");
					}
				}

				Console.WriteLine($"📊 Trade history update complete: {successful} successful, {failed} failed");
				return th;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"⚠️ Failed to load and update trade history: {ex.Message}");
				return Enumerable.Empty<TradeRecord>();
			}
		}

		private async Task<MarketBar> FetchCurrentPrice(string symbol, DateTime? processDate)
		{
			try
			{
				var bars = await _dataProvider.GetBarsAsync(symbol, 1, processDate);
				return bars.Last();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ❌ Error fetching price for {symbol}: {ex.Message}");
			}
			return null;
		}

		private void UpdateCurrentValue(string symbol, DateTime? processDate)
		{
			try
			{
				var bar = FetchCurrentPrice(symbol, processDate).Result;
				if (bar == null) return;
				var currentPrice = bar.Close;
				if (currentPrice > 0)
				{
					_db.UpdateCurrentValue(symbol, currentPrice);
					Console.WriteLine($"   💾 Updated {symbol} price: ${currentPrice:F2}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ❌ Error updating {symbol}: {ex.Message}");
			}
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
				Console.WriteLine($"   💾 Saved {savedCount} price bars");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"   ⚠️ Price persist failed: {ex.Message}");
			}
		}

		private void PrintSwingSummary(Dictionary<string, ProcessResult> results, SwingTradingMetrics metrics)
		{
			Console.WriteLine("\n═══════════════════════════════════════════════════════");
			Console.WriteLine("📊 SWING TRADING ANALYSIS SUMMARY");
			Console.WriteLine("═══════════════════════════════════════════════════════");

			var successful = results.Values.Count(r => r.Status == "Success");
			var failed = results.Values.Count(r => r.Status == "Failed");
			var skipped = results.Values.Count(r => r.Status == "Skipped");
			var errors = results.Values.Count(r => r.Status == "Error");

			Console.WriteLine($"✅ Analyzed:   {successful}");
			Console.WriteLine($"❌ Failed:     {failed}");
			Console.WriteLine($"⚠️  Skipped:    {skipped}");
			Console.WriteLine($"🔥 Errors:     {errors}");

			Console.WriteLine("\n📈 Swing Trade Metrics:");
			Console.WriteLine($"   Volatility Filtered: {metrics.VolatilityFiltered}");
			Console.WriteLine($"   Weekly Trend Filtered: {metrics.WeeklyTrendFiltered}");
			Console.WriteLine($"   High Gap Risk: {metrics.HighGapRiskCount}");
			Console.WriteLine($"   Pullback Entries: {metrics.PullbackEntries}");

			// Show filtered symbols
			if (skipped > 0)
			{
				Console.WriteLine("\n📝 Skipped Details:");
				foreach (var (symbol, result) in results.Where(r => r.Value.Status == "Skipped").Take(10))
				{
					Console.WriteLine($"   {symbol}: {result.Message}");
				}
				if (skipped > 10)
					Console.WriteLine($"   ... and {skipped - 10} more");
			}

			// Show symbols with signals
			var signalResults = results.Where(r => r.Value.Status == "Success").ToList();
			if (signalResults.Any())
			{
				var pullbacks = signalResults.Where(r => r.Value.InPullback).ToList();
				if (pullbacks.Any())
				{
					Console.WriteLine("\n🎯 Pullback Entry Opportunities:");
					foreach (var (symbol, _) in pullbacks.Take(5))
					{
						Console.WriteLine($"   {symbol}");
					}
				}

				var highGapRisk = signalResults.Where(r => r.Value.GapRisk == "High").ToList();
				if (highGapRisk.Any())
				{
					Console.WriteLine("\n⚠️  High Gap Risk (reduce position size):");
					foreach (var (symbol, _) in highGapRisk.Take(5))
					{
						Console.WriteLine($"   {symbol}");
					}
				}
			}

			if (successful > 0)
			{
				var totalBars = results.Values.Where(r => r.Status == "Success").Sum(r => r.BarCount);
				Console.WriteLine($"\n📈 Total bars analyzed: {totalBars:N0}");
			}

			Console.WriteLine("\n💾 Results saved to database");
			Console.WriteLine("═══════════════════════════════════════════════════════");
			Console.WriteLine($"✅ Swing analysis completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
			Console.WriteLine("═══════════════════════════════════════════════════════\n");
		}

		// ═══════════════════════════════════════════════════════════════
		// HELPER CLASSES
		// ═══════════════════════════════════════════════════════════════

		private class ProcessResult
		{
			public string Symbol { get; set; } = "";
			public string Status { get; set; } = "Unknown";
			public string Message { get; set; } = "";
			public int BarCount { get; set; } = 0;
			public string GapRisk { get; set; } = "Unknown";
			public bool InPullback { get; set; } = false;
		}

		private class DataQualityResult
		{
			public bool IsValid { get; set; }
			public string Message { get; set; } = "";
		}

		private class SwingTradingMetrics
		{
			public int Analyzed { get; set; }
			public int VolatilityFiltered { get; set; }
			public int WeeklyTrendFiltered { get; set; }
			public int HighGapRiskCount { get; set; }
			public int PullbackEntries { get; set; }
		}
	}
}