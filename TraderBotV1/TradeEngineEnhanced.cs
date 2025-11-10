using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	/// <summary>
	/// IMPROVED Trade Engine - Optimized for balanced buy/sell signals
	/// Changes: Lower thresholds, relaxed requirements, better signal generation
	/// </summary>
	public class TradeEngineEnhanced
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;
		private readonly EmailNotificationService? _emailService;
		private readonly List<TradingSignal> _sessionSignals;

		// ⚖️ IMPROVED THRESHOLDS - More balanced signal generation
		private const int MIN_VOTES_REQUIRED = 4;              // ⭐ REDUCED: Need only 2 strategies (was 3)
		private const decimal MIN_STRATEGY_CONFIDENCE = 0.45m; // ⭐ REDUCED: 35% to count as a vote (was 45%)
		private const decimal MIN_FINAL_CONFIDENCE = 0.48m;    // ⭐ REDUCED: 40% for final decision (was 48%)
		private const decimal MIN_QUALITY_SCORE = 0.50m;       // ⭐ REDUCED: 20% quality threshold (was 30%)
		private const int MIN_STRATEGIES_FOR_ENTRY = 4;        // Need 2 strategies minimum (was 3)

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
			List<decimal>? volumes,
			List<decimal>? opens,
			DateTime lastBarDate)
		{
			if (closes.Count < 100)
			{
				Console.WriteLine($"⚠️ Insufficient data for {symbol} (need 100+ bars)");
				return;
			}

			if (volumes == null || volumes.Count == 0)
			{
				Console.WriteLine("   ⚠️ No volume data - using estimation");
				volumes = Indicators.EstimateVolume(closes, highs, lows);
			}
			// Create opens if not provided
			if (opens == null || opens.Count != closes.Count)
			{
				opens = closes.Select((c, i) => i > 0 ? closes[i - 1] : c).ToList();
			}

			int idx = closes.Count - 1;

			// ═══════════════════════════════════════════════════════════════
			// STEP 1: MARKET REGIME ANALYSIS (INFORMATIONAL ONLY)
			// ═══════════════════════════════════════════════════════════════

			var regime = Indicators.DetectMarketRegime(closes, highs, lows);

			Console.WriteLine($"\n📊 {symbol} Market Analysis:");
			Console.WriteLine($"   Regime: {regime.Description} (confidence: {regime.RegimeConfidence:P0})");
			Console.WriteLine($"   Trend Strength: {regime.TrendStrength:P2}");
			Console.WriteLine($"   Volatility: {regime.VolatilityLevel:P2}");

			// ⭐ IMPROVED: Only skip EXTREME volatility (>10%)
			if (regime.VolatilityLevel > 0.10m)
			{
				Console.WriteLine($"⚠️ EXTREME volatility ({regime.VolatilityLevel:P2}) - skipping {symbol}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Extreme volatility");
				return;
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 2: MULTI-TIMEFRAME CONFIRMATION (BONUS ONLY)
			// ═══════════════════════════════════════════════════════════════

			var mtf = Indicators.AnalyzeMultiTimeframe(closes, highs, lows);
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
			// STEP 4: EXECUTE IMPROVED STRATEGIES
			// ═══════════════════════════════════════════════════════════════

			// Enhanced Strategies (Higher Priority)
			var s1 = Strategies.TrendFollowingMTF(closes, highs, lows);
			var s2 = volumes != null ?
				Strategies.MeanReversionSR(closes, highs, lows, volumes) :
				Hold("No volume data");
			var s3 = volumes != null ?
				Strategies.BreakoutWithVolume(opens, closes, highs, lows, volumes) :
				Hold("No volume data");
			var s4 = volumes != null ?
				Strategies.MomentumReversalDivergence(closes, highs, lows, volumes) :
				Hold("No volume data");

			// Original Validated Strategies
			var s5 = Strategies.EmaRsi(closes, 9, 21, 14);
			var s6 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s7 = Strategies.AtrBreakout(closes, highs, lows, atr, 9, 21, 14);
			var s8 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

			// Extended Strategies
			var s9 = Strategies.AdxFilter(highs, lows, closes, 14, 18m);  // ⭐ REDUCED ADX threshold to 18 (was 20)
			var s10 = volumes != null ?
				Strategies.VolumeConfirm(closes, volumes, 20, 1.0m) :  // ⭐ REDUCED: 1.0x spike (was 1.1x)
				Hold("No volume");
			var s11 = Strategies.DonchianBreakout(highs, lows, closes, 20);

			// Advanced Strategies (Selected Best Performers)
			var s12 = volumes != null ?
				Strategies.VWAPStrategy(closes, highs, lows, volumes) :
				Hold("No volume");
			var s13 = Strategies.IchimokuCloud(closes, highs, lows);
			var s14 = Strategies.PriceActionTrend(closes, highs, lows);

			// New Enhanced Strategies
			var s15 = volumes != null ?
				Strategies.SupertrendStrategy(highs, lows, closes, 10, 3m) :
				Hold("No data");

			var s16 = volumes != null ?
				Strategies.MeanReversionMFI(closes, highs, lows, volumes, 14, 20) :
				Hold("No volume");

			var s17 = volumes != null ?
				Strategies.TripleMomentumStrategy(closes, highs, lows, volumes, 12) :
				Hold("No volume");

			var s18 = volumes != null ?
				Strategies.SupportResistanceBounce(closes, highs, lows, volumes) :
				Hold("No volume");

			var s19 = volumes != null && opens != null ?
				Strategies.GapTradingStrategy(opens, closes, highs, lows, volumes, 0.005m) :
				Hold("No data");

			var s20 = volumes != null ?
				Strategies.CMFMomentumStrategy(closes, highs, lows, volumes, 20) :
				Hold("No volume");

			var s21 = volumes != null ?
				Strategies.ForceIndexBreakout(closes, volumes, 13) :
				Hold("No volume");

			// Update allSignals array
			var allSignals = new[] {
										s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14,
										s15, s16, s17, s18, s19, s20, s21  // Add new strategies
									};

			// ═══════════════════════════════════════════════════════════════
			// STEP 5: VOTE COUNTING WITH WEIGHTED CONFIDENCE
			// ═══════════════════════════════════════════════════════════════

			var buySignals = new List<(StrategySignal signal, int index)>();
			var sellSignals = new List<(StrategySignal signal, int index)>();

			for (int i = 0; i < allSignals.Length; i++)
			{
				var signal = allSignals[i];
				// ⭐ IMPROVED: Lower threshold to capture more signals
				if (signal.Signal == "Buy" && signal.Strength >= MIN_STRATEGY_CONFIDENCE)
				{
					buySignals.Add((signal, i));
				}
				else if (signal.Signal == "Sell" && signal.Strength >= MIN_STRATEGY_CONFIDENCE)
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
					// Enhanced strategies (0-3) get 1.2x weight (reduced from 1.3x for more balance)
					decimal weight = index < 4 ? 1.2m : 1.0m;
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
			// STEP 6: QUALITY SCORE CALCULATION (MORE LENIENT)
			// ═══════════════════════════════════════════════════════════════

			string preliminaryDirection = "Hold";
			if (buyVotes > sellVotes && buyVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Buy";
			else if (sellVotes > buyVotes && sellVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Sell";

			decimal qualityScore = 0m;
			decimal qualityScoreSimple  = 0m;
			if (preliminaryDirection != "Hold")
			{
				qualityScore = Strategies.CalculateTradeQualityScore(
					opens, closes, highs, lows, volumes ?? new List<decimal>(), preliminaryDirection);

				//qualityScoreSimple = SimplifiedQualityScore.Calculate(
				//	opens, closes, highs, lows, volumes ?? new List<decimal>(), preliminaryDirection);

				//if (qualityScoreSimple > qualityScore)
				//{
				//	qualityScore = qualityScoreSimple;
				//}
				//// For detailed breakdown:
				//var (score, breakdown) = SimplifiedQualityScore.CalculateWithDetails(
				//	opens, closes, highs, lows, volumes ?? new List<decimal>(), preliminaryDirection);

				//Console.WriteLine($"   Quality Breakdown: {breakdown}");

				Console.WriteLine($"   Trade Quality Score: {qualityScore:P0}");
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 7: FINAL DECISION WITH IMPROVED FILTERS
			// ═══════════════════════════════════════════════════════════════

			string finalSignal = "Hold";
			decimal finalConfidence = 0m;
			string finalReason = "No consensus";

			// MTF alignment bonus
			decimal mtfBonus = 0m;
			bool mtfAligned = false;

			// ⭐ IMPROVED Decision Logic - Lower thresholds for more signals
			if (buyVotes >= MIN_VOTES_REQUIRED && avgBuyConfidence >= MIN_FINAL_CONFIDENCE)
			{
				// Check MTF alignment for bonus
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Up")
				{
					mtfAligned = true;
					mtfBonus = 0.10m; // 10% bonus for MTF alignment
				}

				// ⭐ IMPROVED: Accept signals with lower quality if votes and confidence are strong
				if (qualityScore >= MIN_QUALITY_SCORE && (buyVotes >= 3 && avgBuyConfidence >= 0.50m))
				{
					finalSignal = "Buy";
					finalConfidence = Math.Min(avgBuyConfidence + mtfBonus, 1.0m);
					finalReason = $"Buy consensus: {buyVotes} strategies";
					if (mtfAligned) finalReason += " + MTF aligned";
					if (qualityScore >= 0.40m) finalReason += " + high quality";
				}
				else
				{
					finalReason = $"Quality too low: {qualityScore:P0} (need {MIN_QUALITY_SCORE:P0})";
				}
			}
			else if (sellVotes >= MIN_VOTES_REQUIRED && avgSellConfidence >= MIN_FINAL_CONFIDENCE)
			{
				// Check MTF alignment for bonus
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Down")
				{
					mtfAligned = true;
					mtfBonus = 0.10m;
				}

				// ⭐ IMPROVED: Accept signals with lower quality if votes and confidence are strong
				if (qualityScore >= MIN_QUALITY_SCORE && (sellVotes >= 3 && avgSellConfidence >= 0.50m))
				{
					finalSignal = "Sell";
					finalConfidence = Math.Min(avgSellConfidence + mtfBonus, 1.0m);
					finalReason = $"Sell consensus: {sellVotes} strategies";
					if (mtfAligned) finalReason += " + MTF aligned";
					if (qualityScore >= 0.40m) finalReason += " + high quality";
				}
				else
				{
					finalReason = $"Quality too low: {qualityScore:P0} (need {MIN_QUALITY_SCORE:P0})";
				}
			}
			else if (buyVotes > 0 || sellVotes > 0)
			{
				if (qualityScore < MIN_QUALITY_SCORE && preliminaryDirection != "Hold")
				{
					finalReason = $"Quality score too low: {qualityScore:P0} < {MIN_QUALITY_SCORE:P0}";
				}
				else if (avgBuyConfidence < MIN_FINAL_CONFIDENCE && avgSellConfidence < MIN_FINAL_CONFIDENCE)
				{
					finalReason = $"Confidence too low (buy:{avgBuyConfidence:P0}, sell:{avgSellConfidence:P0}, need:{MIN_FINAL_CONFIDENCE:P0})";
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
				var srLevels = Indicators.FindSupportResistance(highs, lows, closes);

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
			decimal equity = 10000m;
			decimal riskValue = equity * _riskPercent;

			// ⭐ IMPROVED: Less aggressive quality adjustment
			decimal adjustedRisk = riskValue * Math.Max(qualityScore * 1.3m, 0.7m);  // 70-130% of base risk
			decimal qty = Math.Max(1, Math.Floor(adjustedRisk / stopDistance));

			//qty = 10;

			// ═══════════════════════════════════════════════════════════════
			// STEP 9: STORE SIGNAL & LOG
			// ═══════════════════════════════════════════════════════════════

			if (finalSignal != "Hold")
			{
				_sessionSignals.Add(new TradingSignal
				{
					Symbol = symbol,
					Signal = finalSignal,
					Confidence = finalConfidence,
					EntryPrice = entry,
					Quantity = qty,
					StopDistance = stopDistance,
					ConfirmedStrategies = finalSignal == "Buy" ? buyVotes : sellVotes,
					Reason = finalReason,
					Timestamp = DateTime.Now,
					LastBarDate = lastBarDate
				});
			}

			// Detailed logging
			LogEnhancedSignals(symbol, new Dictionary<string, StrategySignal>
			{
				{ "TrendMTF", s1 }, { "MeanRevSR", s2 }, { "BreakoutVol", s3 },
				{ "MomentumDiv", s4 }, { "EMA+RSI", s5 }, { "Bollinger", s6 },
				{ "ATR", s7 }, { "MACD", s8 }, { "ADX", s9 }, { "Volume", s10 },
				{ "Donchian", s11 }, { "VWAP", s12 }, { "Ichimoku", s13 },
				{ "PriceAction", s14 },
				{ "Supertrend", s15 }, { "MeanRevMFI", s16 }, { "TripleMomentum", s17 },
				{ "SRBounce", s18 }, { "GapTrading", s19 }, { "CMFMomentum", s20 },
				{ "ForceIndex", s21 }
			});

			_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Consensus", finalSignal,
				$"{finalReason} | conf={finalConfidence:P0} | quality={qualityScore:P0}");

			if (finalSignal != "Hold")
			{
				_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Entry", finalSignal,
					$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}, quality={qualityScore:P0}");

				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, (long)qty, entry,finalConfidence,qualityScore, lastBarDate);
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
				Console.WriteLine($"   Bar Date: {lastBarDate:yyyy-MM-dd HH:mm:ss}");
			}
			else
			{
				Console.WriteLine($"   Action: HOLD - {finalReason}");
			}

			Console.WriteLine($"   ✓ Completed analysis for {symbol}");
		}

		public async System.Threading.Tasks.Task SendSessionNotificationsAsync(string recipientEmail)
		{
			if (_emailService == null)
			{
				Console.WriteLine("⚠️ Email service not configured");
				return;
			}

			var buySignals = _sessionSignals.Where(s => s.Signal == "Buy" && s.Confidence > .7m).ToList();

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
			Console.WriteLine($"\n📋 Strategy Signals (Improved Engine):");

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
					string strength = s.Strength >= 0.70m ? "STRONG" :
									 s.Strength >= 0.45m ? "Good" :
									 s.Strength >= 0.30m ? "Moderate" : "Weak";
					Console.WriteLine($"   {icon} {name,-14}: {s.Signal,-4} {strength,-8} ({s.Strength:P0}) - {s.Reason}");

					_db.InsertSignal(symbol, DateTime.UtcNow, name, s.Signal,
						$"{s.Strength:F2}|{s.Reason}");
				}
			}
		}

		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);
	}
}