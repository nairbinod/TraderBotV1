using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	/// <summary>
	/// Enhanced Trade Engine with improved signal quality and false signal reduction
	/// </summary>
	public class TradeEngineEnhanced
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;
		private readonly EmailNotificationService? _emailService;
		private readonly List<TradingSignal> _sessionSignals;

		// ENHANCED THRESHOLDS
		private const int MIN_VOTES_REQUIRED = 5;           // Increased from 4
		private const decimal MIN_CONFIDENCE = 0.75m;       // Increased from 0.7
		private const decimal MIN_QUALITY_SCORE = 0.65m;    // NEW: Minimum quality score
		private const int MIN_STRATEGIES_FOR_ENTRY = 3;     // NEW: Min strategies agreeing

		public TradeEngineEnhanced(SqliteStorage db, decimal riskPercent = 0.01m,
			EmailNotificationService? emailService = null)
		{
			_db = db;
			_riskPercent = riskPercent;
			_emailService = emailService;
			_sessionSignals = new List<TradingSignal>();
		}

		public void EvaluateAndLog(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal>? volumes = null,
			List<DateTime>? timestamps = null,
			List<decimal>? opens = null)
		{
			if (closes.Count < 100)
			{
				Console.WriteLine($"⚠️ Insufficient data for {symbol} (need 100+ bars)");
				return;
			}

			// Create opens if not provided
			if (opens == null || opens.Count != closes.Count)
			{
				opens = closes.Select((c, i) => i > 0 ? closes[i - 1] : c).ToList();
			}

			int idx = closes.Count - 1;

			// ═══════════════════════════════════════════════════════════════
			// STEP 1: MARKET REGIME ANALYSIS (CRITICAL FILTER)
			// ═══════════════════════════════════════════════════════════════

			var regime = IndicatorsEnhanced.DetectMarketRegime(closes, highs, lows);

			Console.WriteLine($"\n📊 {symbol} Market Analysis:");
			Console.WriteLine($"   Regime: {regime.Description} (confidence: {regime.RegimeConfidence:P0})");
			Console.WriteLine($"   Trend Strength: {regime.TrendStrength:P2}");
			Console.WriteLine($"   Volatility: {regime.VolatilityLevel:P2}");

			// Skip in unfavorable conditions
			if (regime.Regime == IndicatorsEnhanced.MarketRegime.Volatile)
			{
				Console.WriteLine($"⚠️ Extreme volatility - skipping {symbol}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Extreme volatility");
				return;
			}

			if (regime.Regime == IndicatorsEnhanced.MarketRegime.Quiet)
			{
				Console.WriteLine($"⚠️ Very quiet market - skipping {symbol}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Quiet market");
				return;
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 2: MULTI-TIMEFRAME CONFIRMATION
			// ═══════════════════════════════════════════════════════════════

			var mtf = IndicatorsEnhanced.AnalyzeMultiTimeframe(closes, highs, lows);
			Console.WriteLine($"   MTF Analysis: {mtf.Reason}");
			Console.WriteLine($"   MTF Confidence: {mtf.Confidence:P0}");

			// ═══════════════════════════════════════════════════════════════
			// STEP 3: CALCULATE ALL INDICATORS (ONCE)
			// ═══════════════════════════════════════════════════════════════

			var emaShort = Indicators.EMAList(closes, 9);
			var emaLong = Indicators.EMAList(closes, 21);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 20, 2);
			var atr = Indicators.ATRList(highs, lows, closes, 14);

			// ═══════════════════════════════════════════════════════════════
			// STEP 4: EXECUTE ENHANCED STRATEGIES
			// ═══════════════════════════════════════════════════════════════

			// NEW Enhanced Strategies (Higher Priority)
			var s1 = StrategiesEnhanced.TrendFollowingMTF(closes, highs, lows);
			var s2 = volumes != null ?
				StrategiesEnhanced.MeanReversionSR(closes, highs, lows, volumes) :
				Hold("No volume data");
			var s3 = volumes != null ?
				StrategiesEnhanced.BreakoutWithVolume(opens, closes, highs, lows, volumes) :
				Hold("No volume data");
			var s4 = volumes != null ?
				StrategiesEnhanced.MomentumReversalDivergence(closes, highs, lows, volumes) :
				Hold("No volume data");

			// Original Validated Strategies
			var s5 = Strategies.EmaRsi(closes, 9, 21, 14);
			var s6 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s7 = Strategies.AtrBreakout(closes, highs, lows, atr, 9, 21, 14);
			var s8 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

			// Extended Strategies
			var s9 = StrategiesExtended.AdxFilter(highs, lows, closes, 14, 25m);
			var s10 = volumes != null ?
				StrategiesExtended.VolumeConfirm(closes, volumes, 20, 1.5m) :
				Hold("No volume");
			var s11 = StrategiesExtended.DonchianBreakout(highs, lows, closes, 20);

			// Advanced Strategies (Selected Best Performers)
			var s12 = volumes != null ?
				StrategiesAdvanced.VWAPStrategy(closes, highs, lows, volumes) :
				Hold("No volume");
			var s13 = StrategiesAdvanced.IchimokuCloud(closes, highs, lows);
			var s14 = StrategiesAdvanced.PriceActionTrend(closes, highs, lows);

			var allSignals = new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14 };

			// ═══════════════════════════════════════════════════════════════
			// STEP 5: VOTE COUNTING WITH WEIGHTED CONFIDENCE
			// ═══════════════════════════════════════════════════════════════

			var buySignals = new List<(StrategySignal signal, int index)>();
			var sellSignals = new List<(StrategySignal signal, int index)>();

			for (int i = 0; i < allSignals.Length; i++)
			{
				var signal = allSignals[i];
				if (signal.Signal == "Buy" && signal.Strength >= MIN_CONFIDENCE)
				{
					buySignals.Add((signal, i));
				}
				else if (signal.Signal == "Sell" && signal.Strength >= MIN_CONFIDENCE)
				{
					sellSignals.Add((signal, i));
				}
			}

			// Calculate weighted confidence (enhanced strategies get higher weight)
			decimal CalculateWeightedConfidence(List<(StrategySignal signal, int index)> signals)
			{
				if (signals.Count == 0) return 0m;

				decimal totalWeight = 0m;
				decimal weightedSum = 0m;

				foreach (var (signal, index) in signals)
				{
					// Enhanced strategies (0-3) get 1.5x weight
					decimal weight = index < 4 ? 1.5m : 1.0m;
					weightedSum += signal.Strength * weight;
					totalWeight += weight;
				}

				return weightedSum / totalWeight;
			}

			decimal avgBuyConfidence = CalculateWeightedConfidence(buySignals);
			decimal avgSellConfidence = CalculateWeightedConfidence(sellSignals);

			int buyVotes = buySignals.Count;
			int sellVotes = sellSignals.Count;

			Console.WriteLine($"\n🔍 Signal Analysis:");
			Console.WriteLine($"   Buy votes: {buyVotes} (weighted confidence: {avgBuyConfidence:P0})");
			Console.WriteLine($"   Sell votes: {sellVotes} (weighted confidence: {avgSellConfidence:P0})");
			Console.WriteLine($"   Required votes: {MIN_VOTES_REQUIRED}");

			// ═══════════════════════════════════════════════════════════════
			// STEP 6: QUALITY SCORE CALCULATION
			// ═══════════════════════════════════════════════════════════════

			string preliminaryDirection = "Hold";
			if (buyVotes > sellVotes && buyVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Buy";
			else if (sellVotes > buyVotes && sellVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Sell";

			decimal qualityScore = 0m;
			if (preliminaryDirection != "Hold")
			{
				qualityScore = StrategiesEnhanced.CalculateTradeQualityScore(
					opens, closes, highs, lows, volumes ?? new List<decimal>(), preliminaryDirection);

				Console.WriteLine($"   Trade Quality Score: {qualityScore:P0}");
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 7: FINAL DECISION WITH ENHANCED FILTERS
			// ═══════════════════════════════════════════════════════════════

			string finalSignal = "Hold";
			decimal finalConfidence = 0m;
			string finalReason = "No consensus";

			// Enhanced Decision Logic
			if (buyVotes >= MIN_VOTES_REQUIRED &&
				buyVotes > sellVotes &&
				avgBuyConfidence >= MIN_CONFIDENCE &&
				qualityScore >= MIN_QUALITY_SCORE)
			{
				// Additional MTF filter for buy
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Up")
				{
					finalSignal = "Buy";
					finalConfidence = (avgBuyConfidence * 0.5m + qualityScore * 0.5m);
					finalReason = $"{buyVotes} strategies, quality={qualityScore:P0}, MTF aligned";
				}
				else
				{
					finalReason = $"{buyVotes} buy votes but MTF not aligned ({mtf.Reason})";
				}
			}
			else if (sellVotes >= MIN_VOTES_REQUIRED &&
					sellVotes > buyVotes &&
					avgSellConfidence >= MIN_CONFIDENCE &&
					qualityScore >= MIN_QUALITY_SCORE)
			{
				// Additional MTF filter for sell
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Down")
				{
					finalSignal = "Sell";
					finalConfidence = (avgSellConfidence * 0.5m + qualityScore * 0.5m);
					finalReason = $"{sellVotes} strategies, quality={qualityScore:P0}, MTF aligned";
				}
				else
				{
					finalReason = $"{sellVotes} sell votes but MTF not aligned ({mtf.Reason})";
				}
			}
			else if (buyVotes > 0 || sellVotes > 0)
			{
				if (qualityScore < MIN_QUALITY_SCORE && preliminaryDirection != "Hold")
				{
					finalReason = $"Quality score too low: {qualityScore:P0} < {MIN_QUALITY_SCORE:P0}";
				}
				else
				{
					finalReason = $"Insufficient votes (buy:{buyVotes}, sell:{sellVotes}, need:{MIN_VOTES_REQUIRED})";
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 8: POSITION SIZING & RISK MANAGEMENT
			// ═══════════════════════════════════════════════════════════════

			decimal lastClose = closes[idx];
			decimal atrVal = atr.LastOrDefault();
			decimal entry = lastClose;

			// Enhanced stop loss calculation using support/resistance
			decimal stopDistance = atrVal > 0 ? atrVal * 1.5m : lastClose * 0.02m;

			if (finalSignal != "Hold")
			{
				var srLevels = IndicatorsEnhanced.FindSupportResistance(highs, lows, closes);

				if (finalSignal == "Buy")
				{
					entry = Math.Round(lastClose * 1.001m, 2);

					// Find nearest support for stop loss
					var nearestSupport = srLevels
						.Where(l => l.IsSupport && l.Level < lastClose)
						.OrderByDescending(l => l.Level)
						.FirstOrDefault();

					if (nearestSupport != null)
					{
						decimal srStop = lastClose - nearestSupport.Level;
						// Use S/R stop if it's reasonable (not too wide or tight)
						if (srStop > lastClose * 0.01m && srStop < lastClose * 0.05m)
						{
							stopDistance = srStop;
						}
					}
				}
				else if (finalSignal == "Sell")
				{
					entry = Math.Round(lastClose * 0.999m, 2);

					// Find nearest resistance for stop loss
					var nearestResistance = srLevels
						.Where(l => !l.IsSupport && l.Level > lastClose)
						.OrderBy(l => l.Level)
						.FirstOrDefault();

					if (nearestResistance != null)
					{
						decimal srStop = nearestResistance.Level - lastClose;
						if (srStop > lastClose * 0.01m && srStop < lastClose * 0.05m)
						{
							stopDistance = srStop;
						}
					}
				}
			}

			// Position sizing based on quality score
			decimal equity = 100000m;
			decimal riskValue = equity * _riskPercent;

			// Adjust risk based on quality score
			decimal adjustedRisk = riskValue * Math.Max(qualityScore, 0.5m);
			decimal qty = Math.Max(1, Math.Floor(adjustedRisk / stopDistance));

			// ═══════════════════════════════════════════════════════════════
			// STEP 9: STORE SIGNAL & LOG
			// ═══════════════════════════════════════════════════════════════

			if (finalSignal == "Buy")
			{
				_sessionSignals.Add(new TradingSignal
				{
					Symbol = symbol,
					Signal = finalSignal,
					Confidence = finalConfidence,
					EntryPrice = entry,
					Quantity = qty,
					StopDistance = stopDistance,
					ConfirmedStrategies = buyVotes,
					Reason = finalReason,
					Timestamp = DateTime.UtcNow
				});
			}

			// Detailed logging
			LogEnhancedSignals(symbol, new Dictionary<string, StrategySignal>
			{
				{ "TrendMTF", s1 }, { "MeanRevSR", s2 }, { "BreakoutVol", s3 },
				{ "MomentumDiv", s4 }, { "EMA+RSI", s5 }, { "Bollinger", s6 },
				{ "ATR", s7 }, { "MACD", s8 }, { "ADX", s9 }, { "Volume", s10 },
				{ "Donchian", s11 }, { "VWAP", s12 }, { "Ichimoku", s13 },
				{ "PriceAction", s14 }
			});

			_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Consensus", finalSignal,
				$"{finalReason} | conf={finalConfidence:P0} | quality={qualityScore:P0}");

			if (finalSignal != "Hold")
			{
				_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Entry", finalSignal,
					$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}, quality={qualityScore:P0}");

				DateTime? barDate = timestamps != null && timestamps.Count > 0
					? timestamps[timestamps.Count - 1]
					: null;

				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, (long)qty, entry, barDate);
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 10: CONSOLE OUTPUT
			// ═══════════════════════════════════════════════════════════════

			Console.WriteLine($"\n✅ {symbol} FINAL DECISION:");
			Console.WriteLine($"   Signal: {finalSignal}");
			Console.WriteLine($"   Confidence: {finalConfidence:P0}");
			Console.WriteLine($"   Quality Score: {qualityScore:P0}");
			Console.WriteLine($"   Reason: {finalReason}");

			if (finalSignal != "Hold")
			{
				Console.WriteLine($"   Entry: ${entry:F2}");
				Console.WriteLine($"   Quantity: {qty:F0} shares");
				Console.WriteLine($"   Stop Distance: ${stopDistance:F2} ({stopDistance / entry:P2})");
				Console.WriteLine($"   Risk: ${adjustedRisk:F2} (adjusted by quality)");
				Console.WriteLine($"   Position Value: ${entry * qty:N2}");

				if (timestamps != null && timestamps.Count > 0)
				{
					Console.WriteLine($"   Bar Date: {timestamps[timestamps.Count - 1]:yyyy-MM-dd HH:mm:ss}");
				}
			}
			else
			{
				Console.WriteLine($"   Action: HOLD - {finalReason}");
			}
		}

		public async System.Threading.Tasks.Task SendSessionNotificationsAsync(string recipientEmail)
		{
			if (_emailService == null)
			{
				Console.WriteLine("⚠️ Email service not configured");
				return;
			}

			var buySignals = _sessionSignals.Where(s => s.Signal == "Buy").ToList();

			if (buySignals.Count == 0)
			{
				Console.WriteLine("📧 No buy signals to send");
				return;
			}

			Console.WriteLine($"\n📧 Sending email notification for {buySignals.Count} high-quality buy signal(s)...");

			bool success = await _emailService.SendBuySignalNotificationAsync(recipientEmail, buySignals);

			if (success)
			{
				Console.WriteLine($"✅ Email notification sent successfully to {recipientEmail}");
			}
			else
			{
				Console.WriteLine($"❌ Failed to send email notification");
			}
		}

		public List<TradingSignal> GetSessionSignals() => new List<TradingSignal>(_sessionSignals);

		public void ClearSessionSignals() => _sessionSignals.Clear();

		private void LogEnhancedSignals(string symbol, Dictionary<string, StrategySignal> signals)
		{
			Console.WriteLine($"\n📋 Strategy Signals (Enhanced Engine):");

			var groupedSignals = new[]
			{
				("Enhanced", signals.Take(4).ToList()),
				("Core", signals.Skip(4).Take(4).ToList()),
				("Extended", signals.Skip(8).Take(3).ToList()),
				("Advanced", signals.Skip(11).ToList())
			};

			foreach (var (group, sigs) in groupedSignals)
			{
				if (!sigs.Any()) continue;
				Console.WriteLine($"\n   {group} Strategies:");

				foreach (var (name, s) in sigs)
				{
					string icon = s.Signal == "Buy" ? "🟢" : s.Signal == "Sell" ? "🔴" : "⚪";
					string strength = s.Strength >= 0.8m ? "STRONG" :
									 s.Strength >= 0.7m ? "Good" : "Weak";
					Console.WriteLine($"   {icon} {name,-14}: {s.Signal,-4} {strength,-6} ({s.Strength:P0}) - {s.Reason}");

					_db.InsertSignal(symbol, DateTime.UtcNow, name, s.Signal,
						$"{s.Strength:F2}|{s.Reason}");
				}
			}
		}

		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);
	}
}