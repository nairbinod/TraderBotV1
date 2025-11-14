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
		// Balance between signal frequency and quality
		private const int MIN_VOTES_REQUIRED = 6;              // Keep at 5
		private const decimal MIN_STRATEGY_CONFIDENCE = 0.45m; // ↑ Slight increase (was 45%)
		private const decimal MIN_FINAL_CONFIDENCE = 0.55m;    // ↑ 58% final (was 55%)
		private const decimal MIN_QUALITY_SCORE = 0.52m;       // ↓ Lower threshold (was 52%)
		private const int MIN_STRATEGIES_FOR_ENTRY = 6;        // Keep at 5

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

			// NEW STRATEGIES (Add after s21)
			var s22 = Strategies.WilliamsRReversal(highs, lows, closes, 14);

			var s23 = Strategies.ParabolicSARTrend(highs, lows, closes);

			var s24 = volumes != null ?
				Strategies.KeltnerChannelBreakout(highs, lows, closes, volumes) :
				Hold("No volume");

			var s25 = volumes != null ?
				Strategies.OBVDivergence(closes, volumes, 20) :
				Hold("No volume");

			var s26 = Strategies.AroonTrendChange(highs, lows, closes, 25);

			var s27 = volumes != null ?
				Strategies.RocMomentumBurst(closes, volumes, 12) :
				Hold("No volume");

			var s28 = Strategies.TSICrossover(closes);

			var s29 = Strategies.VortexTrend(highs, lows, closes);

			var s30 = volumes != null ?
				Strategies.MultiIndicatorConfluence(highs, lows, closes, volumes) :
				Hold("No volume");

			// Update allSignals array
			var allSignals = new[] {
										s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14,
										s15, s16, s17, s18, s19, s20, s21,  // Add new strategies
										s22, s23, s24, s25, s26, s27, s28, s29, s30  // NEW strategies
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
			if (preliminaryDirection != "Hold")
			{
				// Option B: Run both and compare (for testing)
				decimal oldScore = SimplifiedQualityScore.Calculate(
										opens, closes, highs, lows, volumes, preliminaryDirection);
				decimal newScore = ImprovedQualityScore.Calculate(
										opens, closes, highs, lows, volumes, preliminaryDirection);

				// Use new score but log both for comparison
				qualityScore = newScore;
				Console.WriteLine($"   Quality: OLD={oldScore:P0} vs NEW={newScore:P0}");

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
				if ((qualityScore >= MIN_QUALITY_SCORE && buyVotes >= MIN_VOTES_REQUIRED) || (buyVotes >= MIN_VOTES_REQUIRED && avgBuyConfidence >= MIN_FINAL_CONFIDENCE))
				{
					// ⭐ NEW: Validate before accepting signal
					bool isValid = ValidateSignalQuality(
									"Buy", buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence,
									qualityScore, closes, highs, lows, volumes ?? new List<decimal>());
					// ⭐ NEW: Require core strategy confirmation
					bool hasCoreConfirmation = HasCoreStrategyConfirmation(buySignals, "Buy");

					//isValid = true;

					if (!hasCoreConfirmation && !(buyVotes >= MIN_VOTES_REQUIRED && avgBuyConfidence >= MIN_FINAL_CONFIDENCE))
					{
						finalReason = "Need 1 core strategy OR 5+ votes at 52%+";
						Console.WriteLine($"   ❌ Rejected: Only new strategies voting, no core confirmation");
					}
					else if (isValid)
					{
						finalSignal = "Buy";
						finalConfidence = Math.Min(avgBuyConfidence + mtfBonus, 1.0m);
						finalReason = $"Buy consensus: {buyVotes} strategies";
						if (mtfAligned) finalReason += " + MTF aligned";
						if (qualityScore >= MIN_QUALITY_SCORE) finalReason += " + high quality";
					}
					else
					{
						finalReason = "Buy signal failed validation checks";
					}
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
				if ((qualityScore >= MIN_QUALITY_SCORE && sellVotes >= MIN_VOTES_REQUIRED) || (sellVotes >= 5 && avgSellConfidence >= MIN_FINAL_CONFIDENCE))
				{
					finalSignal = "Sell";
					finalConfidence = Math.Min(avgSellConfidence + mtfBonus, 1.0m);
					finalReason = $"Sell consensus: {sellVotes} strategies";
					if (mtfAligned) finalReason += " + MTF aligned";
					if (qualityScore >= MIN_FINAL_CONFIDENCE) finalReason += " + high quality";
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
				{ "ForceIndex", s21 },
				{ "WilliamsR", s22 }, { "PSAR", s23 }, { "Keltner", s24 },
				{ "OBV", s25 }, { "Aroon", s26 }, { "ROC", s27 },
				{ "TSI", s28 }, { "Vortex", s29 }, { "Confluence", s30 }
			});

			_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Consensus", finalSignal,
				$"{finalReason} | conf={finalConfidence:P0} | quality={qualityScore:P0}");

			if (finalSignal != "Hold")
			{
				_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Entry", finalSignal,
					$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}, quality={qualityScore:P0}");

				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, (long)qty, entry, finalConfidence, qualityScore, lastBarDate);
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

		/// <summary>
		/// Validates signal quality before execution
		/// Returns false if signal fails quality checks
		/// </summary>
		private bool ValidateSignalQuality(
			string finalSignal,
			int buyVotes,
			int sellVotes,
			decimal avgBuyConfidence,
			decimal avgSellConfidence,
			decimal qualityScore,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (finalSignal == "Hold") return true;

			int idx = closes.Count - 1;

			// CHECK 1: Conflicting votes
			int agreeingVotes = finalSignal == "Buy" ? buyVotes : sellVotes;
			int conflictingVotes = finalSignal == "Buy" ? sellVotes : buyVotes;

			if (conflictingVotes >= 6)  // Too much disagreement
			{
				Console.WriteLine($"   ❌ VALIDATION FAILED: {conflictingVotes} conflicting votes");
				return false;
			}

			// CHECK 2: Vote ratio
			if (agreeingVotes > 0 && conflictingVotes > 0)
			{
				decimal voteRatio = (decimal)agreeingVotes / conflictingVotes;
				if (voteRatio < 1.4m)  // Need at least 2:1 agreement
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: Vote ratio only {voteRatio:F1}:1");
					return false;
				}
			}

			// CHECK 3: Extreme RSI
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiValue = rsi[^1];
				if (finalSignal == "Buy" && rsiValue > 77m)
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: RSI too high for buy ({rsiValue:F1})");
					return false;
				}
				if (finalSignal == "Sell" && rsiValue < 23m)
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: RSI too low for sell ({rsiValue:F1})");
					return false;
				}
			}

			// CHECK 4: Chasing price
			decimal priceChange = (closes[idx] - closes[idx - 1]) / closes[idx - 1];
			if (finalSignal == "Buy" && priceChange > 0.035m)
			{
				Console.WriteLine($"   ❌ VALIDATION FAILED: Chasing rally (+{priceChange:P2})");
				return false;
			}
			if (finalSignal == "Sell" && priceChange < -0.035m)
			{
				Console.WriteLine($"   ❌ VALIDATION FAILED: Chasing drop ({priceChange:P2})");
				return false;
			}

			// CHECK 5: Extreme volatility
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal volatility = (atr[^1] / closes[idx]) * 100m;
				if (volatility > 8.5m)
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: Volatility too high ({volatility:F2}%)");
					return false;
				}
			}

			// CHECK 6: Counter-trend quality requirement
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			bool counterTrend = (finalSignal == "Buy" && context.IsDowntrend) ||
								(finalSignal == "Sell" && context.IsUptrend);

			if (counterTrend && qualityScore < 0.48m)
			{
				Console.WriteLine($"   ❌ VALIDATION FAILED: Counter-trend needs quality ≥70% (got {qualityScore:P0})");
				return false;
			}

			// CHECK 7: Trend strength requirement for trend-following signals
			if (!counterTrend)  // Only for trend-following trades
			{
				if (finalSignal == "Buy" && context.IsUptrend)
				{
					if (context.TrendStrength < 0.008m)  // Less than 1% trend strength
					{
						Console.WriteLine($"   ❌ VALIDATION FAILED: Weak uptrend ({context.TrendStrength:P2})");
						return false;
					}
				}
				else if (finalSignal == "Sell" && context.IsDowntrend)
				{
					if (context.TrendStrength < 0.008m)
					{
						Console.WriteLine($"   ❌ VALIDATION FAILED: Weak downtrend ({context.TrendStrength:P2})");
						return false;
					}
				}
			}

			// CHECK 8: Minimum volume requirement
			if (volumes.Count > idx && idx >= 20)
			{
				var avgVol = volumes.Skip(Math.Max(0, idx - 20)).Take(20).Average();
				var currentVol = volumes[idx];

				// Current volume must be at least 50% of average
				if (currentVol < avgVol * 0.35m)
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: Volume too low ({currentVol / avgVol:P0} of avg)");
					return false;
				}
			}

			// CHECK 9: Don't chase large gaps
			if (idx > 0)
			{
				decimal gapPercent = Math.Abs(closes[idx] - closes[idx - 1]) / closes[idx - 1];

				if (gapPercent > 0.08m)  // More than 5% gap
				{
					Console.WriteLine($"   ❌ VALIDATION FAILED: Large gap detected ({gapPercent:P2}) - avoid chasing");
					return false;
				}
			}

			// CHECK 10: Quality must align with vote strength
			// More votes should correlate with higher quality
			int totalVotes = buyVotes + sellVotes;
			int strongVotes = finalSignal == "Buy" ? buyVotes : sellVotes;

			decimal expectedQuality = 0.30m + (strongVotes * 0.05m);  // 30% + 5% per vote

			if (qualityScore < expectedQuality)
			{
				Console.WriteLine($"   ⚠️  WARNING: Quality ({qualityScore:P0}) lower than expected for {strongVotes} votes");
				// Don't fail, just warn - some mean reversion setups might be valid
			}

			Console.WriteLine($"   ✅ VALIDATION PASSED: All quality checks OK");
			return true;
		}

		// ═══════════════════════════════════════════════════════════════
		// FIX 3: STRATEGY BLACKLIST
		// Temporarily disable problematic strategies
		// ═══════════════════════════════════════════════════════════════

		public static HashSet<int> GetBlacklistedStrategyIndices()
		{
			// Add strategy indices that are generating too many false signals
			// Example: If Williams R (s22, index 21) is too aggressive:
			return new HashSet<int>
			{
				// Uncomment indices of strategies generating false signals:
				// 21,  // Williams R - Too sensitive?
				// 23,  // Keltner - Too many breakouts?
				// 26,  // ROC - Too aggressive?
			};
		}

		public static bool IsStrategyBlacklisted(int index)
		{
			return GetBlacklistedStrategyIndices().Contains(index);
		}
		/// <summary>
		/// Checks if at least one proven core strategy confirms the signal
		/// This prevents new/experimental strategies from generating signals alone
		/// </summary>
		private bool HasCoreStrategyConfirmation(
			List<(StrategySignal signal, int index)> signals,
			string direction)
		{
			// IMPROVED: Require at least 2 core strategies OR 1 core + high quality
			var coreStrategyIndices = new HashSet<int>
										{
											0,  // TrendFollowingMTF
											1,  // MeanReversionSR  
											4,  // EmaRsi
											5,  // BollingerMeanReversion
											7,  // MacdDivergence
											12, // VWAP - proven strategy, add to core
										};

			var coreSignals = signals.Where(s =>
				coreStrategyIndices.Contains(s.index) &&
				s.signal.Signal == direction &&
				s.signal.Strength >= 0.45m).ToList();  // STRICT: 45% minimum

			bool hasCoreConfirmation = coreSignals.Count >= 2;  // STRICT: Require 2+ core strategies

			if (!hasCoreConfirmation)
			{
				Console.WriteLine($"   ⚠️  STRICT: Need 2+ core strategies at 45%+ (have {coreSignals.Count})");
			}

			return hasCoreConfirmation;
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