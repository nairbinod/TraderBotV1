using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	/// <summary>
	/// BALANCED SWING TRADING ENGINE
	/// Optimized to generate more quality signals while maintaining good win rates
	/// 
	/// Key Balanced Features:
	/// 1. Moderate vote requirements (6 votes vs 7 conservative)
	/// 2. Balanced confidence thresholds (50% vs 55% conservative)
	/// 3. Quality requirements (48% vs 52% conservative)
	/// 4. Wider acceptable volatility range (0.6% to 8.5%)
	/// 5. Lower trend strength requirement (1.0% vs 1.2%)
	/// 6. More lenient choppiness tolerance (65% vs 60%)
	/// 7. Lower regime confidence (50% vs 55%)
	/// 8. Relaxed validation checks for more signals
	/// 9. Wider RSI extremes (22-78 vs 25-75)
	/// 10. Lower momentum requirement (1.2% vs 1.5%)
	/// </summary>
	public class TradeEngineConservative
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;
		private readonly EmailNotificationService? _emailService;
		private readonly List<TradingSignal> _sessionSignals;

		// ⭐ BALANCED THRESHOLDS - Optimized for more signals with quality
		private const int MIN_VOTES_REQUIRED = 6;              // ⭐ 6 strategies for balanced signals
		private const decimal MIN_STRATEGY_CONFIDENCE = 0.40m; // ⭐ 40% individual strategy confidence
		private const decimal MIN_FINAL_CONFIDENCE = 0.50m;    // ⭐ 50% average confidence
		private const decimal MIN_QUALITY_SCORE = 0.48m;       // ⭐ 48% quality score
		private const int MIN_STRATEGIES_FOR_ENTRY = 6;        // ⭐ 6 minimum strategies

		// ⭐ BALANCED: More lenient filters to allow more quality signals
		private const decimal MIN_VOTE_DOMINANCE = 0.60m;      // ⭐ 60% vote dominance (down from 65%)
		private const decimal MIN_TREND_STRENGTH = 0.010m;     // ⭐ 1.0% trend strength (down from 1.2%)
		private const decimal MIN_SIGNAL_GAP = 0.10m;          // ⭐ 10% signal gap (down from 12%)
		private const decimal MAX_CHOPPINESS = 0.65m;          // ⭐ 65% max choppiness (up from 60%)

		public TradeEngineConservative(SqliteStorage db, decimal riskPercent = 0.015m,
			EmailNotificationService? emailService = null)
		{
			_db = db;
			_riskPercent = riskPercent;
			_emailService = emailService;
			_sessionSignals = new List<TradingSignal>();
		}

		public List<TradingSignal> GetSessionSignals() => _sessionSignals;

		public void EvaluateAndLog(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal>? volumes,
			List<decimal>? opens,
			DateTime lastBarDate)
		{
			if (closes.Count < 120)
			{
				Console.WriteLine($"⚠️ Insufficient data for {symbol} (need 120+ bars)");
				return;
			}

			if (volumes == null || volumes.Count == 0)
			{
				Console.WriteLine("   ⚠️ No volume data - using estimation");
				volumes = Indicators.EstimateVolume(closes, highs, lows);
			}

			if (opens == null || opens.Count != closes.Count)
			{
				opens = closes.Select((c, i) => i > 0 ? closes[i - 1] : c).ToList();
			}

			int idx = closes.Count - 1;

			// ═══════════════════════════════════════════════════════════════
			// STEP 1: MARKET REGIME FILTERING
			// ═══════════════════════════════════════════════════════════════

			var regime = Indicators.DetectMarketRegime(closes, highs, lows);

			Console.WriteLine($"\n📊 {symbol} Analysis (BALANCED ENGINE):");
			Console.WriteLine($"   Regime: {regime.Description}");
			Console.WriteLine($"   Trend: {regime.TrendStrength:P2} | Volatility: {regime.VolatilityLevel:P2}");

			// ⭐ BALANCED: Wider volatility range for more signals
			if (regime.VolatilityLevel > 0.085m)  // ⭐ 8.5% (up from 7.5%)
			{
				Console.WriteLine($"❌ High volatility ({regime.VolatilityLevel:P2})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "High volatility");
				return;
			}

			if (regime.VolatilityLevel < 0.006m)  // ⭐ 0.6% (down from 0.8%)
			{
				Console.WriteLine($"❌ Low volatility ({regime.VolatilityLevel:P2})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Low volatility");
				return;
			}

			// ⭐ REALISTIC: More lenient choppiness threshold
			var choppiness = CalculateChoppiness(highs, lows, closes, 14);
			if (choppiness > MAX_CHOPPINESS)
			{
				Console.WriteLine($"❌ Choppy market ({choppiness:P0} > {MAX_CHOPPINESS:P0})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Choppiness", "Hold", $"Choppy {choppiness:P0}");
				return;
			}
			Console.WriteLine($"   Choppiness: {choppiness:P0} ✓");

			// ⭐ BALANCED: Lower regime confidence requirement
			if (regime.RegimeConfidence < 0.50m)  // ⭐ 50% (down from 55%)
			{
				Console.WriteLine($"❌ Low regime confidence ({regime.RegimeConfidence:P0})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Unclear regime");
				return;
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 2: MTF ANALYSIS (BONUS, NOT MANDATORY)
			// ═══════════════════════════════════════════════════════════════

			var mtf = Indicators.AnalyzeMultiTimeframe(closes, highs, lows);
			Console.WriteLine($"   MTF: {mtf.CurrentTFTrend} (aligned: {mtf.IsAligned}, conf: {mtf.Confidence:P0})");

			// ⭐ BALANCED: MTF alignment is BONUS, with lower threshold
			decimal mtfBonus = 0m;
			if (mtf.IsAligned && mtf.Confidence >= 0.45m)  // ⭐ 45% (down from 50%)
			{
				mtfBonus = 0.05m;  // 5% bonus if MTF aligned
				Console.WriteLine($"   ✅ MTF Bonus: +5%");
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 3: TREND STRENGTH VERIFICATION
			// ═══════════════════════════════════════════════════════════════

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// ⭐ REALISTIC: Lower trend strength requirement
			if (context.TrendStrength < MIN_TREND_STRENGTH)
			{
				Console.WriteLine($"❌ Weak trend ({context.TrendStrength:P2})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Trend", "Hold", $"Weak trend");
				return;
			}

			if (!context.IsUptrend && !context.IsDowntrend)
			{
				Console.WriteLine($"❌ No clear trend direction");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Trend", "Hold", "No clear trend");
				return;
			}

			Console.WriteLine($"   ✅ Trend: {(context.IsUptrend ? "UP" : "DOWN")} @ {context.TrendStrength:P2}");

			// ═══════════════════════════════════════════════════════════════
			// STEP 4: CALCULATE INDICATORS
			// ═══════════════════════════════════════════════════════════════

			var emaShort = Indicators.EMAList(closes, 20);
			var emaLong = Indicators.EMAList(closes, 50);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 25, 2);
			var atr = Indicators.ATRList(highs, lows, closes, 20);

			// ═══════════════════════════════════════════════════════════════
			// STEP 5: EXECUTE ALL STRATEGIES
			// ═══════════════════════════════════════════════════════════════

			var s1 = Strategies.TrendFollowingMTF(closes, highs, lows);
			var s2 = Strategies.MeanReversionSR(closes, highs, lows, volumes);
			var s3 = Strategies.BreakoutWithVolume(opens, closes, highs, lows, volumes);
			var s4 = Strategies.MomentumReversalDivergence(closes, highs, lows, volumes);
			var s5 = Strategies.EmaRsi(closes, 20, 50, 14);
			var s6 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s7 = Strategies.AtrBreakout(closes, highs, lows, atr, 20, 50, 14);
			var s8 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);
			var s9 = Strategies.AdxFilter(highs, lows, closes, 14, 20m);  // ⭐ 20 (down from 23)
			var s10 = Strategies.VolumeConfirm(closes, volumes, 30, 1.0m);  // ⭐ 1.0x (down from 1.1x)
			var s11 = Strategies.DonchianBreakout(highs, lows, closes, 25);
			var s12 = Strategies.VWAPStrategy(closes, highs, lows, volumes);
			var s13 = Strategies.IchimokuCloud(closes, highs, lows);
			var s14 = Strategies.PriceActionTrend(closes, highs, lows);
			var s15 = Strategies.SupertrendStrategy(highs, lows, closes, 10, 3m);
			var s16 = Strategies.MeanReversionMFI(closes, highs, lows, volumes, 14, 20);
			var s17 = Strategies.TripleMomentumStrategy(closes, highs, lows, volumes, 12);
			var s18 = Strategies.SupportResistanceBounce(closes, highs, lows, volumes);
			var s19 = Strategies.GapTradingStrategy(opens, closes, highs, lows, volumes, 0.005m);
			var s20 = Strategies.CMFMomentumStrategy(closes, highs, lows, volumes, 20);
			var s21 = Strategies.ForceIndexBreakout(closes, volumes, 13);
			var s22 = Strategies.WilliamsRReversal(highs, lows, closes, 14);
			var s23 = Strategies.ParabolicSARTrend(highs, lows, closes);
			var s24 = Strategies.KeltnerChannelBreakout(highs, lows, closes, volumes);
			var s25 = Strategies.OBVDivergence(closes, volumes, 20);
			var s26 = Strategies.AroonTrendChange(highs, lows, closes, 25);
			var s27 = Strategies.RocMomentumBurst(closes, volumes, 12);
			var s28 = Strategies.TSICrossover(closes);
			var s29 = Strategies.VortexTrend(highs, lows, closes);
			var s30 = Strategies.MultiIndicatorConfluence(highs, lows, closes, volumes);
			var s31 = Strategies.VolatilitySqueeze(closes, highs, lows, volumes);
			var s32 = Strategies.ElderTripleScreen(closes, highs, lows);
			var s33 = Strategies.ElderRayStrategy(highs, lows, closes, volumes);
			var s34 = Strategies.ChoppinessFilter(highs, lows, closes);
			var s35 = Strategies.WeeklyTrendFilter(closes, highs, lows);
			var s36 = Strategies.LinearRegressionBreakout(closes, volumes);
			var s37 = Strategies.HeikinAshiTrend(opens, highs, lows, closes);
			var s38 = Strategies.FibonacciRetracement(highs, lows, closes);

			var allSignals = new List<(StrategySignal signal, int index)> {
				(s1,0), (s2,1), (s3,2), (s4,3), (s5,4), (s6,5), (s7,6), (s8,7), (s9,8), (s10,9),
				(s11,10), (s12,11), (s13,12), (s14,13), (s15,14), (s16,15), (s17,16), (s18,17),
				(s19,18), (s20,19),(s21,20), (s22,21), (s23,22), (s24,23), (s25,24), (s26,25),
				(s27,26), (s28,27), (s29,28), (s30,29),
				(s31,30), (s32,31), (s33,32), (s34,33), (s35,34), (s36,35), (s37,36), (s38,37)
			};

			// ═══════════════════════════════════════════════════════════════
			// STEP 6: VOTE COUNTING
			// ═══════════════════════════════════════════════════════════════

			// ⭐ REALISTIC: Slightly lower confidence threshold
			var buySignals = allSignals
				.Where(s => s.signal.Signal == "Buy" && s.signal.Strength >= MIN_STRATEGY_CONFIDENCE)
				.ToList();

			var sellSignals = allSignals
				.Where(s => s.signal.Signal == "Sell" && s.signal.Strength >= MIN_STRATEGY_CONFIDENCE)
				.ToList();

			decimal CalculateWeightedConfidence(List<(StrategySignal signal, int index)> signals)
			{
				if (signals.Count == 0) return 0m;
				decimal totalWeight = 0m;
				decimal weightedSum = 0m;
				foreach (var (signal, index) in signals)
				{
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

			Console.WriteLine($"\n🔍 Votes: Buy={buyVotes}@{avgBuyConfidence:P0} | Sell={sellVotes}@{avgSellConfidence:P0}");
			Console.WriteLine($"   Need: {MIN_VOTES_REQUIRED} votes @ {MIN_FINAL_CONFIDENCE:P0}");

			//// ⭐ DIAGNOSTIC: Show why rejected
			//if (buyVotes < MIN_VOTES_REQUIRED && sellVotes < MIN_VOTES_REQUIRED)
			//{
			//	Console.WriteLine($"   📊 Enable diagnostics to see detailed rejection reasons");
			//	SignalDiagnostics.AnalyzeRejection(symbol, closes, highs, lows, volumes,
			//		buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence, 0m);
			//}

			// ⭐ REALISTIC: Check signal gap
			decimal signalGap = Math.Abs(avgBuyConfidence - avgSellConfidence);
			if (buyVotes > 0 && sellVotes > 0 && signalGap < MIN_SIGNAL_GAP)
			{
				Console.WriteLine($"❌ Signal conflict (gap: {signalGap:P0} < {MIN_SIGNAL_GAP:P0})");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Conflict", "Hold", "Buy/Sell conflict");
				return;
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 7: QUALITY SCORE
			// ═══════════════════════════════════════════════════════════════

			string preliminaryDirection = "Hold";
			if (buyVotes > sellVotes && buyVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Buy";
			else if (sellVotes > buyVotes && sellVotes >= MIN_STRATEGIES_FOR_ENTRY)
				preliminaryDirection = "Sell";

			decimal qualityScore = 0m;
			if (preliminaryDirection != "Hold")
			{
				qualityScore = ImprovedQualityScore.CalculateSwingQualityScore(
					opens, closes, highs, lows, volumes, preliminaryDirection);
				Console.WriteLine($"   Quality: {qualityScore:P0} (need {MIN_QUALITY_SCORE:P0})");
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 8: FINAL DECISION
			// ═══════════════════════════════════════════════════════════════

			string finalSignal = "Hold";
			decimal finalConfidence = 0m;
			string finalReason = "No consensus";

			if (buyVotes >= MIN_VOTES_REQUIRED && avgBuyConfidence >= MIN_FINAL_CONFIDENCE)
			{
				if (!context.IsUptrend)
				{
					Console.WriteLine($"❌ Buy in non-uptrend");
					finalReason = "Buy but not uptrend";
				}
				else if (qualityScore < MIN_QUALITY_SCORE)
				{
					Console.WriteLine($"❌ Quality too low ({qualityScore:P0})");
					finalReason = $"Quality {qualityScore:P0} < {MIN_QUALITY_SCORE:P0}";
				}
				else
				{
					bool isValid = ValidateRealisticConservative(
						"Buy", buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence,
						qualityScore, closes, highs, lows, volumes, context);

					bool hasCoreConfirmation = buySignals.Any(s => s.index <= 8);

					decimal voteDominance = (decimal)buyVotes / (buyVotes + sellVotes);
					if (voteDominance < MIN_VOTE_DOMINANCE)
					{
						Console.WriteLine($"❌ Vote dominance ({voteDominance:P0})");
						finalReason = $"Dominance {voteDominance:P0} < {MIN_VOTE_DOMINANCE:P0}";
					}
					else if (!hasCoreConfirmation)
					{
						Console.WriteLine($"❌ No core strategy");
						finalReason = "Need core strategy";
					}
					else if (isValid)
					{
						finalSignal = "Buy";
						finalConfidence = Math.Min(avgBuyConfidence + mtfBonus, 1.0m);
						finalReason = $"Buy: {buyVotes}@{avgBuyConfidence:P0} Q:{qualityScore:P0}";
						Console.WriteLine($"   ✅ SIGNAL APPROVED");
					}
					else
					{
						finalReason = "Failed validation";
					}
				}
			}
			else if (sellVotes >= MIN_VOTES_REQUIRED && avgSellConfidence >= MIN_FINAL_CONFIDENCE)
			{
				if (!context.IsDowntrend)
				{
					Console.WriteLine($"❌ Sell in non-downtrend");
					finalReason = "Sell but not downtrend";
				}
				else if (qualityScore < MIN_QUALITY_SCORE)
				{
					Console.WriteLine($"❌ Quality too low ({qualityScore:P0})");
					finalReason = $"Quality {qualityScore:P0} < {MIN_QUALITY_SCORE:P0}";
				}
				else
				{
					bool isValid = ValidateRealisticConservative(
						"Sell", buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence,
						qualityScore, closes, highs, lows, volumes, context);

					bool hasCoreConfirmation = sellSignals.Any(s => s.index <= 8);

					decimal voteDominance = (decimal)sellVotes / (buyVotes + sellVotes);
					if (voteDominance < MIN_VOTE_DOMINANCE)
					{
						Console.WriteLine($"❌ Vote dominance ({voteDominance:P0})");
						finalReason = $"Dominance {voteDominance:P0} < {MIN_VOTE_DOMINANCE:P0}";
					}
					else if (!hasCoreConfirmation)
					{
						Console.WriteLine($"❌ No core strategy");
						finalReason = "Need core strategy";
					}
					else if (isValid)
					{
						finalSignal = "Sell";
						finalConfidence = Math.Min(avgSellConfidence + mtfBonus, 0.95m);
						finalReason = $"Sell: {sellVotes}@{avgSellConfidence:P0} Q:{qualityScore:P0}";
						Console.WriteLine($"   ✅ SIGNAL APPROVED");
					}
					else
					{
						finalReason = "Failed validation";
					}
				}
			}
			else
			{
				if (qualityScore < MIN_QUALITY_SCORE && preliminaryDirection != "Hold")
					finalReason = $"Quality {qualityScore:P0}";
				else if (avgBuyConfidence < MIN_FINAL_CONFIDENCE && avgSellConfidence < MIN_FINAL_CONFIDENCE)
					finalReason = $"Low confidence";
				else
					finalReason = $"Insufficient votes";
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 9: POSITION SIZING
			// ═══════════════════════════════════════════════════════════════

			decimal lastClose = closes[idx];
			decimal atrVal = atr.LastOrDefault();
			decimal entry = lastClose;
			decimal stopDistance = atrVal > 0 ? atrVal * 3.0m : lastClose * 0.04m;

			if (finalSignal != "Hold")
			{
				var srLevels = Indicators.FindSupportResistance(highs, lows, closes);

				if (finalSignal == "Buy")
				{
					entry = Math.Round(lastClose * 1.001m, 2);
					var nearestSupport = srLevels
						.Where(l => l.IsSupport && l.Level < lastClose)
						.OrderByDescending(l => l.Level)
						.FirstOrDefault();

					if (nearestSupport != null)
					{
						decimal srStop = lastClose - nearestSupport.Level;
						if (srStop > lastClose * 0.02m && srStop < lastClose * 0.08m)
							stopDistance = srStop;
					}

					stopDistance = Math.Max(stopDistance, lastClose * 0.03m);
					stopDistance = Math.Min(stopDistance, lastClose * 0.08m);
				}
				else if (finalSignal == "Sell")
				{
					entry = Math.Round(lastClose * 0.999m, 2);
					var nearestResistance = srLevels
						.Where(l => !l.IsSupport && l.Level > lastClose)
						.OrderBy(l => l.Level)
						.FirstOrDefault();

					if (nearestResistance != null)
					{
						decimal srStop = nearestResistance.Level - lastClose;
						if (srStop > lastClose * 0.02m && srStop < lastClose * 0.08m)
							stopDistance = srStop;
					}

					stopDistance = Math.Max(stopDistance, lastClose * 0.03m);
					stopDistance = Math.Min(stopDistance, lastClose * 0.08m);
				}
			}

			decimal equity = 10000m;
			decimal riskValue = equity * _riskPercent;
			decimal adjustedRisk = riskValue * Math.Max(qualityScore * 1.4m, 0.7m);
			decimal qty = Math.Max(1, Math.Floor(adjustedRisk / stopDistance));

			// ═══════════════════════════════════════════════════════════════
			// STEP 10: OUTPUT
			// ═══════════════════════════════════════════════════════════════

			if (finalSignal != "Hold")
			{
				decimal trendMultiplier = context.TrendStrength > 0.02m ? 1.2m : 1.0m;

				decimal profitTarget1 = finalSignal == "Buy"
					? entry + (stopDistance * 2.5m * trendMultiplier)
					: entry - (stopDistance * 2.5m * trendMultiplier);

				decimal profitTarget2 = finalSignal == "Buy"
					? entry + (stopDistance * 4.0m * trendMultiplier)
					: entry - (stopDistance * 4.0m * trendMultiplier);

				decimal stopLoss = finalSignal == "Buy"
					? entry - stopDistance
					: entry + stopDistance;

				int maxHoldDays = context.TrendStrength > 0.02m && qualityScore > 0.65m ? 15 :
								 context.TrendStrength < 0.01m || qualityScore < 0.55m ? 7 : 10;

				Console.WriteLine($"\n✅ {finalSignal} SIGNAL for {symbol} (REALISTIC CONSERVATIVE)");
				Console.WriteLine($"   Confidence: {finalConfidence:P0} | Quality: {qualityScore:P0}");
				Console.WriteLine($"   Entry: ${entry:F2} | Stop: ${stopLoss:F2}");
				Console.WriteLine($"   Target 1: ${profitTarget1:F2} | Target 2: ${profitTarget2:F2}");
				Console.WriteLine($"   Max Hold: {maxHoldDays} days");

				_db.InsertSignal(symbol, lastBarDate, "RealisticConservative", finalSignal, finalReason);

				var tradingSignal = new TradingSignal
				{
					Symbol = symbol,
					Direction = finalSignal,
					Entry = entry,
					StopLoss = stopLoss,
					Target1 = profitTarget1,
					Target2 = profitTarget2,
					Quantity = (int)qty,
					Confidence = finalConfidence,
					Quality = qualityScore,
					SignalDate = lastBarDate,
					MaxHoldDays = maxHoldDays,
					Reason = finalReason
				};

				_sessionSignals.Add(tradingSignal);
			}
			else
			{
				Console.WriteLine($"\n⏸️  HOLD: {finalReason}");
				_db.InsertSignal(symbol, lastBarDate, "BalancedEngine", "Hold", finalReason);
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
				{ "TSI", s28 }, { "Vortex", s29 }, { "Confluence", s30 },
				{ "VolatilitySqueeze", s31 }, { "ElderTripleScreen", s32 },
				{ "ElderRay", s33 }, { "Choppiness", s34 }, { "WeeklyTrend", s35 },
				{ "LinRegBreakout", s36 }, { "HeikinAshi", s37 }, { "Fibonacci", s38 }
			});

			_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Consensus", finalSignal,
				$"{finalReason} | conf={finalConfidence:P0} | quality={qualityScore:P0}");

			if (finalSignal != "Hold")
			{
				_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Entry", finalSignal,
					$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}, quality={qualityScore:P0}");

				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, (long)qty, entry, finalConfidence, qualityScore, lastBarDate);
			}
		}

		private bool ValidateRealisticConservative(
			string direction,
			int buyVotes,
			int sellVotes,
			decimal buyConfidence,
			decimal sellConfidence,
			decimal quality,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			SignalValidator.MarketContext context)
		{
			int idx = closes.Count - 1;

			// ⭐ BALANCED: Only critical validation checks

			// 1. EMA separation (0.8% minimum instead of 1%)
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);
			decimal emaSeparation = Math.Abs(ema20[idx] - ema50[idx]) / ema50[idx];

			if (emaSeparation < 0.008m)  // ⭐ 0.8% (down from 1%)
			{
				Console.WriteLine($"   ❌ EMA too close ({emaSeparation:P2})");
				return false;
			}

			// 2. RSI not extreme wrong direction (wider range for more signals)
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiVal = rsi[^1];
				if (direction == "Buy" && rsiVal > 78m)  // ⭐ 78 (up from 75)
				{
					Console.WriteLine($"   ❌ RSI too high ({rsiVal:F0})");
					return false;
				}
				if (direction == "Sell" && rsiVal < 22m)  // ⭐ 22 (down from 25)
				{
					Console.WriteLine($"   ❌ RSI too low ({rsiVal:F0})");
					return false;
				}
			}

			// 3. Recent momentum (7-day with lower threshold)
			if (idx >= 7)
			{
				decimal price7DaysAgo = closes[idx - 7];
				decimal momentum7D = (closes[idx] - price7DaysAgo) / price7DaysAgo;

				bool momentumMatch = (direction == "Buy" && momentum7D > 0) ||
									(direction == "Sell" && momentum7D < 0);

				if (!momentumMatch || Math.Abs(momentum7D) < 0.012m)  // ⭐ 1.2% (down from 1.5%)
				{
					Console.WriteLine($"   ❌ Weak momentum ({momentum7D:P2})");
					return false;
				}
			}

			// 4. Volume declining check (more lenient)
			if (volumes.Count > 20 && idx >= 20)
			{
				var avg5 = volumes.Skip(idx - 5).Take(5).Average();
				var avg20 = volumes.Skip(idx - 20).Take(20).Average();

				if (avg5 < avg20 * 0.6m)  // ⭐ 60% (down from 70%)
				{
					Console.WriteLine($"   ❌ Volume declining ({avg5 / avg20:P0})");
					return false;
				}
			}

			return true;
		}

		private decimal CalculateChoppiness(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
		{
			if (closes.Count < period + 1) return 0.50m;

			int idx = closes.Count - 1;
			decimal atr = 0m;

			for (int i = idx; i > idx - period; i--)
			{
				if (i > 0)
				{
					decimal tr = Math.Max(highs[i] - lows[i],
						Math.Max(Math.Abs(highs[i] - closes[i - 1]),
								Math.Abs(lows[i] - closes[i - 1])));
					atr += tr;
				}
			}

			decimal highestHigh = highs.Skip(idx - period).Take(period).Max();
			decimal lowestLow = lows.Skip(idx - period).Take(period).Min();
			decimal range = highestHigh - lowestLow;

			if (range == 0) return 1.0m;

			decimal chop = 100m * (decimal)Math.Log10((double)(atr / range)) / (decimal)Math.Log10((double)period);
			return Math.Min(Math.Max(chop / 100m, 0m), 1m);
		}

		private StrategySignal Hold(string reason) => new("Hold", 0m, reason);


		public async System.Threading.Tasks.Task SendSessionNotificationsAsync(string recipientEmail)
		{
			if (_emailService == null)
			{
				Console.WriteLine("⚠️ Email service not configured");
				return;
			}
			// ⭐ BALANCED: Send signals with 60%+ confidence
			var buySignals = _sessionSignals.Where(s => s.Direction == "Buy" && s.Confidence > .65m).ToList();

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
	}
}