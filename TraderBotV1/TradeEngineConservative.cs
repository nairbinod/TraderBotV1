using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;
using static TraderBotV1.SignalValidator;

namespace TraderBotV1
{
	public class TradingSignal
	{
		public string Symbol { get; set; } = "";
		public string Direction { get; set; } = "";
		public decimal Entry { get; set; }
		public decimal StopLoss { get; set; }
		public decimal Target1 { get; set; }
		public decimal Target2 { get; set; }
		public int Quantity { get; set; }
		public decimal Confidence { get; set; }
		public decimal Quality { get; set; }
		public DateTime SignalDate { get; set; }
		public int MaxHoldDays { get; set; }
		public string Reason { get; set; } = "";
	}

	/// <summary>
	/// RELAXED SWING TRADING ENGINE - More Signals
	/// Designed to generate more signals for 1-2 week holds
	///
	/// Key Features (RELAXED - generate more signals):
	/// 1. Vote requirements: 3 (lower consensus)
	/// 2. Confidence thresholds: 48% (lower confidence)
	/// 3. Quality requirements: 48% (lower quality bar)
	/// 4. Volatility range: 0.5% to 8% (wider range)
	/// 5. Trend strength: 0.3% (lower trend strength)
	/// 6. Choppiness tolerance: 65% (more tolerance)
	/// 7. Regime confidence: 40% (lower clarity required)
	/// 8. Signal gap: 2% (smaller gap)
	/// 9. Vote dominance: 52% (lower agreement)
	/// 10. Momentum: 0.5% min (no max - allow extended moves)
	/// 11. RSI: 78/22 (allow more momentum)
	/// 12. Composite score: 50% (lower bar)
	/// 13. Confirmations: 2 (same)
	/// 14. ADX threshold: 20 (trending market)
	/// 15. EMA separation: 0.4% (lower trend clarity)
	/// 16. Volume health: 40% (lower volume requirement)
	/// 17. Bollinger conflict: 5% penalty
	///
	/// Goal: Generate more signals while still filtering noise
	/// </summary>
	public partial class TradeEngineConservative
	{
		private readonly SqlServerStorage _db;
		private readonly decimal _riskPercent;
		private readonly EmailNotificationService? _emailService;
		private readonly System.Collections.Concurrent.ConcurrentBag<TradingSignal> _sessionSignals;

		// ═══════════════════════════════════════════════════════════════
		// BALANCED SWING TRADING THRESHOLDS
		// Stricter than ultra-relaxed, but still generates signals
		// ═══════════════════════════════════════════════════════════════

		private const decimal MIN_COMPOSITE_SCORE = 0.55m;     // RELAXED: Allow more signals
		private const int MIN_CONFIRMATIONS = 2;               // Keep at 2

		private const int MIN_VOTES_REQUIRED = 5;              // RELAXED: 3 strategy votes
		private const decimal MIN_STRATEGY_CONFIDENCE = 0.48m; // RELAXED: Lower confidence
		private const decimal MIN_FINAL_CONFIDENCE = 0.48m;    // RELAXED: Lower confidence
		private const decimal MIN_QUALITY_SCORE = 0.48m;       // RELAXED: 48% quality minimum
		private const int MIN_STRATEGIES_FOR_ENTRY = 5;        // Match vote requirement

		private const decimal MIN_VOTE_DOMINANCE = 0.52m;      // RELAXED: 52% vote agreement
		private const decimal MIN_TREND_STRENGTH = 0.003m;     // RELAXED: 0.3% trend strength
		private const decimal MIN_SIGNAL_GAP = 0.02m;          // RELAXED: 2% gap between buy/sell
		private const decimal MAX_CHOPPINESS = 0.65m;          // RELAXED: Higher choppiness tolerance
		private const decimal BOLLINGER_CONFLICT_PENALTY = 0.05m; // RELAXED: 5% penalty

		// Volatility bounds - wider range for swing trades
		private const decimal MIN_VOLATILITY = 0.005m;         // RELAXED: 0.5% minimum
		private const decimal MAX_VOLATILITY = 0.08m;          // RELAXED: 8% maximum

		// Regime confidence
		private const decimal MIN_REGIME_CONFIDENCE = 0.40m;   // RELAXED: 40% regime confidence

		public TradeEngineConservative(SqlServerStorage db, decimal riskPercent = 0.015m,
			EmailNotificationService? emailService = null)
		{
			_db = db;
			_riskPercent = riskPercent;
			_emailService = emailService;
			_sessionSignals = new System.Collections.Concurrent.ConcurrentBag<TradingSignal>();
		}

		public List<TradingSignal> GetSessionSignals() => _sessionSignals.ToList();

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

			Console.WriteLine($"\n📊 {symbol} Analysis (BALANCED SWING):");
			Console.WriteLine($"   Regime: {regime.Description}");
			Console.WriteLine($"   Trend: {regime.TrendStrength:P2} | Volatility: {regime.VolatilityLevel:P2}");

			if (regime.VolatilityLevel > MAX_VOLATILITY)  // 0.08 = 8%
			{
				Console.WriteLine($"❌ High volatility ({regime.VolatilityLevel:P2} > {MAX_VOLATILITY:P2})");
				return;  // Correctly BLOCKS stocks with volatility > 8%
			}

			if (regime.VolatilityLevel < MIN_VOLATILITY)  // 0.006 = 0.6%
			{
				Console.WriteLine($"❌ Low volatility ({regime.VolatilityLevel:P2} < {MIN_VOLATILITY:P2})");
				return;  // Correctly BLOCKS stocks with volatility < 0.6%
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

			// Require clear market conditions
			if (regime.RegimeConfidence < MIN_REGIME_CONFIDENCE)
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

			// MTF alignment bonus
			decimal mtfBonus = 0m;
			if (mtf.IsAligned && mtf.Confidence >= 0.45m)
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
			// STEP 3.5: PRIMARY FILTERS (HIGH WIN-RATE GATE CHECK)
			// ═══════════════════════════════════════════════════════════════

			Console.WriteLine($"\n🎯 Applying Primary Filters...");

			var primaryFilter = Strategies.PrimaryFilters(closes, highs, lows);

			if (primaryFilter.Signal == "Hold")
			{
				Console.WriteLine($"❌ PRIMARY FILTER FAILED: {primaryFilter.Reason}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "PrimaryFilter", "Hold", primaryFilter.Reason);
				return;
			}

			string allowedDirection = primaryFilter.Signal; // "Buy" or "Sell"
			decimal primaryConfidence = primaryFilter.Strength;

			Console.WriteLine($"   ✅ Primary Filter Passed: {allowedDirection}");
			Console.WriteLine($"   ✅ {primaryFilter.Reason}");
			Console.WriteLine($"   ✅ Market Direction Locked: {allowedDirection}");

			// ═══════════════════════════════════════════════════════════════════════════
			// MODIFICATION #2: Filter Strategy Votes by Allowed Direction
			// Location: In STEP 6 (around line 237)
			// REPLACE the existing buySignals and sellSignals with this:
			// ═══════════════════════════════════════════════════════════════════════════

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
			// STEP 5: EXECUTE ALL STRATEGIES IN PARALLEL
			// ═══════════════════════════════════════════════════════════════

			// Strategy results array - initialized to hold all strategy results
			var strategyResults = new StrategySignal[42];

			// Execute all strategies in parallel for maximum performance
			Parallel.Invoke(
				() => strategyResults[0] = Strategies.TrendFollowingMTF(closes, highs, lows),
				() => strategyResults[1] = Strategies.MeanReversionSR(closes, highs, lows, volumes),
				() => strategyResults[2] = Strategies.BreakoutWithVolume(opens, closes, highs, lows, volumes),
				() => strategyResults[3] = Strategies.MomentumReversalDivergence(closes, highs, lows, volumes),
				() => strategyResults[4] = Strategies.EmaRsiEnhanced(closes, highs, lows, 20, 50, 14),
				() => strategyResults[5] = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14),
				() => strategyResults[6] = Strategies.AtrBreakout(closes, highs, lows, atr, 20, 50, 14),
				() => strategyResults[7] = Strategies.MacdDivergenceEnhanced(closes, macd, macdSig, macdHist),
				() => strategyResults[8] = Strategies.AdxFilter(highs, lows, closes, 14, 22m),
				() => strategyResults[9] = Strategies.VolumeConfirm(closes, volumes, 30, 1.3m),
				() => strategyResults[10] = Strategies.DonchianBreakout(highs, lows, closes, 25),
				() => strategyResults[12] = Strategies.IchimokuCloud(closes, highs, lows),
				() => strategyResults[13] = Strategies.PriceActionTrend(closes, highs, lows),
				() => strategyResults[14] = Strategies.SupertrendStrategyEnhanced(highs, lows, closes, 14, 2.5m),
				() => strategyResults[15] = Strategies.MeanReversionMFI(closes, highs, lows, volumes, 14, 20),
				() => strategyResults[16] = Strategies.TripleMomentumStrategy(closes, highs, lows, volumes, 12),
				() => strategyResults[17] = Strategies.SupportResistanceBounce(closes, highs, lows, volumes),
				() => strategyResults[19] = Strategies.CMFMomentumStrategy(closes, highs, lows, volumes, 20),
				() => strategyResults[20] = Strategies.ForceIndexBreakout(closes, volumes, 13),
				() => strategyResults[21] = Strategies.WilliamsRReversal(highs, lows, closes, 14),
				() => strategyResults[22] = Strategies.ParabolicSARTrend(highs, lows, closes),
				() => strategyResults[23] = Strategies.KeltnerChannelBreakout(highs, lows, closes, volumes),
				() => strategyResults[24] = Strategies.OBVDivergence(closes, volumes, 20),
				() => strategyResults[25] = Strategies.AroonTrendChange(highs, lows, closes, 25),
				() => strategyResults[26] = Strategies.RocMomentumBurst(closes, volumes, 12),
				() => strategyResults[27] = Strategies.TSICrossover(closes),
				() => strategyResults[28] = Strategies.VortexTrend(highs, lows, closes),
				() => strategyResults[29] = Strategies.MultiIndicatorConfluence(highs, lows, closes, volumes),
				() => strategyResults[30] = Strategies.VolatilitySqueeze(closes, highs, lows, volumes),
				() => strategyResults[31] = Strategies.ElderTripleScreen(closes, highs, lows),
				() => strategyResults[32] = Strategies.ElderRayStrategy(highs, lows, closes, volumes),
				() => strategyResults[33] = Strategies.ChoppinessFilter(highs, lows, closes),
				() => strategyResults[34] = Strategies.WeeklyTrendFilter(closes, highs, lows),
				() => strategyResults[35] = Strategies.LinearRegressionBreakout(closes, volumes),
				() => strategyResults[36] = Strategies.HeikinAshiTrend(opens, highs, lows, closes),
				() => strategyResults[37] = Strategies.FibonacciRetracement(highs, lows, closes),
				() => strategyResults[39] = Strategies.LinearRegressionSwingStrategy(closes, 20, 2m),
				() => strategyResults[40] = Strategies.MultiFilterConfirmation(closes, highs, lows),
				() => strategyResults[41] = Strategies.CCISwingStrategy(highs, lows, closes, 20)
			);

			// Assign to named variables for compatibility with existing code
			var s1 = strategyResults[0];
			var s2 = strategyResults[1];
			var s3 = strategyResults[2];
			var s4 = strategyResults[3];
			var s5 = strategyResults[4];
			var s6 = strategyResults[5];
			var s7 = strategyResults[6];
			var s8 = strategyResults[7];
			var s9 = strategyResults[8];
			var s10 = strategyResults[9];
			var s11 = strategyResults[10];
			var s13 = strategyResults[12];
			var s14 = strategyResults[13];
			var s15 = strategyResults[14];
			var s16 = strategyResults[15];
			var s17 = strategyResults[16];
			var s18 = strategyResults[17];
			var s20 = strategyResults[19];
			var s21 = strategyResults[20];
			var s22 = strategyResults[21];
			var s23 = strategyResults[22];
			var s24 = strategyResults[23];
			var s25 = strategyResults[24];
			var s26 = strategyResults[25];
			var s27 = strategyResults[26];
			var s28 = strategyResults[27];
			var s29 = strategyResults[28];
			var s30 = strategyResults[29];
			var s31 = strategyResults[30];
			var s32 = strategyResults[31];
			var s33 = strategyResults[32];
			var s34 = strategyResults[33];
			var s35 = strategyResults[34];
			var s36 = strategyResults[35];
			var s37 = strategyResults[36];
			var s38 = strategyResults[37];
			var s40 = strategyResults[39];
			var s41 = strategyResults[40];
			var s42 = strategyResults[41];

			var allSignals = new List<(StrategySignal signal, int index)> {
				(s1,0), (s2,1), (s3,2), (s4,3), (s5,4), (s6,5), (s7,6), (s8,7), (s9,8), (s10,9),(s11,10),
				//(s12,11),
				(s13,12), (s14,13), (s15,14), (s16,15), (s17,16), (s18,17),
				//(s19,18),
				(s20,19),(s21,20), (s22,21), (s23,22), (s24,23), (s25,24), (s26,25),
				(s27,26), (s28,27), (s29,28), (s30,29),
				(s31,30), (s32,31), (s33,32), (s34,33), (s35,34), (s36,35), (s37,36), (s38,37),
				//(s39, 38),
				(s40, 39), (s41, 40), (s42, 41)
			};

			// ═══════════════════════════════════════════════════════════════
			// STEP 6: VOTE COUNTING
			// ═══════════════════════════════════════════════════════════════

			// ⭐ ENHANCED: Only count strategies matching primary filter direction
			var buySignals = allSignals
				.Where(s => s.signal.Signal == "Buy" &&
							s.signal.Strength >= MIN_STRATEGY_CONFIDENCE &&
							allowedDirection == "Buy")  // ⭐ NEW: Must match primary filter
				.ToList();

			var sellSignals = allSignals
				.Where(s => s.signal.Signal == "Sell" &&
							s.signal.Strength >= MIN_STRATEGY_CONFIDENCE &&
							allowedDirection == "Sell")  // ⭐ NEW: Must match primary filter
				.ToList();


			decimal CalculateWeightedConfidence(List<(StrategySignal signal, int index)> signals)
			{
				if (signals.Count == 0) return 0m;

				// ⭐ REBALANCED: Favor earlier entries, reduce late-entry indicators
				var swingWeights = new Dictionary<int, decimal>
									{
										// HIGH WEIGHT - Early/Leading indicators
										{4, 1.5m},   // EmaRsiEnhanced - catches crossovers early
										{7, 1.4m},   // MacdDivergenceEnhanced - divergence is early
										{31, 1.4m},  // ElderTripleScreen
										{34, 1.3m},  // WeeklyTrendFilter

										// MEDIUM-HIGH WEIGHT - Mean reversion (buy on dips)
										{5, 1.4m},   // BollingerMeanReversion ⬆️ (was 1.1)
										{15, 1.3m},  // MeanRevMFI ⬆️ (NEW)
										{1, 1.2m},   // MeanReversionSR ⬆️ (NEW)

										// MEDIUM WEIGHT - Trend confirmation
										{0, 1.1m},   // TrendFollowingMTF ⬇️ (was 1.5)
										{3, 1.2m},   // MomentumReversalDivergence
										{24, 1.1m},  // OBVDivergence
										{32, 1.2m},  // ElderRayStrategy
										{40, 1.2m},  // MultiFilterConfirmation

										// REDUCED WEIGHT - Lagging/Late-entry indicators
										{12, 0.8m},  // IchimokuCloud ⬇️ (was 1.4) - often too late
										{14, 0.7m},  // SupertrendEnhanced ⬇️ (was 1.3) - often extended
										{10, 0.9m},  // DonchianBreakout ⬇️ - breakouts can be late

										// LOW WEIGHT - Very late or unreliable
										{11, 0.5m},  // VWAPStrategy
										{18, 0.3m},  // GapTradingStrategy
										{20, 0.6m},  // ForceIndexBreakout
										{26, 0.5m},  // RocMomentumBurst ⬇️ (was 0.7) - chases momentum
										{38, 0.5m},  // VWAPSwingStrategy
									};

				decimal totalWeight = 0m;
				decimal weightedSum = 0m;

				foreach (var (signal, index) in signals)
				{
					decimal weight = swingWeights.GetValueOrDefault(index, 1.0m);
					weightedSum += signal.Strength * weight;
					totalWeight += weight;
				}

				return totalWeight > 0 ? weightedSum / totalWeight : 0m;
			}

			decimal avgBuyConfidence = CalculateWeightedConfidence(buySignals);
			decimal avgSellConfidence = CalculateWeightedConfidence(sellSignals);
			int buyVotes = buySignals.Count;
			int sellVotes = sellSignals.Count;

			// ═══════════════════════════════════════════════════════════════════════════
			// MODIFICATION #3: Add Enhanced Diagnostics (OPTIONAL)
			// Location: After vote counting (around line 264)
			// Add this to see what strategies are voting:
			// ═══════════════════════════════════════════════════════════════════════════

			// Check for Bollinger conflict (s6 is Bollinger at index 5)
			bool bollingerConflict = false;
			if (allowedDirection == "Buy" && s6.Signal == "Sell" && s6.Strength >= 0.65m)
			{
				bollingerConflict = true;
				Console.WriteLine($"   ⚠️ Bollinger conflict: Sell signal ({s6.Strength:P0}) against Buy consensus");
			}
			else if (allowedDirection == "Sell" && s6.Signal == "Buy" && s6.Strength >= 0.65m)
			{
				bollingerConflict = true;
				Console.WriteLine($"   ⚠️ Bollinger conflict: Buy signal ({s6.Strength:P0}) against Sell consensus");
			}

			Console.WriteLine($"\n🔍 Vote Analysis (Direction: {allowedDirection}):");
			Console.WriteLine($"   Buy Votes: {buyVotes} @ {avgBuyConfidence:P0}");
			Console.WriteLine($"   Sell Votes: {sellVotes} @ {avgSellConfidence:P0}");
			Console.WriteLine($"   Need: {MIN_VOTES_REQUIRED} votes @ {MIN_FINAL_CONFIDENCE:P0}");
			if (bollingerConflict) Console.WriteLine($"   ⚠️ Bollinger conflict penalty: -{BOLLINGER_CONFLICT_PENALTY:P0}");

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
			// STEP 7.5: COMPOSITE SIGNAL QUALITY SCORE
			// ═══════════════════════════════════════════════════════════════

			CompositeSignalScore? compositeScore = null;

			if (preliminaryDirection != "Hold")
			{
				Console.WriteLine($"\n📊 Calculating Composite Signal Score...");

				compositeScore = CalculateCompositeSignalScore(
					closes, highs, lows, volumes, preliminaryDirection, idx);

				Console.WriteLine($"   Total Score: {compositeScore.TotalScore:P0}");
				Console.WriteLine($"   Confirmations: {compositeScore.ConfirmationCount}/8");
				Console.WriteLine($"   Recommendation: {compositeScore.Recommendation}");
				Console.WriteLine($"   Primary Filters: {compositeScore.MeetsPrimaryFilters}");

				// Show what passed
				if (compositeScore.PassedValidations.Length > 0)
				{
					Console.WriteLine($"   ✅ Passed ({compositeScore.PassedValidations.Length}):");
					foreach (var passed in compositeScore.PassedValidations.Take(5))
					{
						Console.WriteLine($"      • {passed}");
					}
				}

				// Show what failed (if any)
				if (compositeScore.FailedValidations.Length > 0 &&
					compositeScore.FailedValidations.Length <= 3)
				{
					Console.WriteLine($"   ❌ Failed ({compositeScore.FailedValidations.Length}):");
					foreach (var failed in compositeScore.FailedValidations)
					{
						Console.WriteLine($"      • {failed}");
					}
				}
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
					Console.WriteLine($"❌ Buy signal in non-uptrend");
					finalReason = "Buy but not uptrend";
				}
				// ⭐ NEW: Use composite score for quality check
				else if (compositeScore != null && compositeScore.TotalScore < MIN_COMPOSITE_SCORE)
				{
					Console.WriteLine($"❌ Composite score too low ({compositeScore.TotalScore:P0} < {MIN_COMPOSITE_SCORE})");
					finalReason = compositeScore.Recommendation;
				}
				// ⭐ NEW: Require minimum confirmations
				else if (compositeScore != null && compositeScore.ConfirmationCount < MIN_CONFIRMATIONS)
				{
					Console.WriteLine($"❌ Insufficient confirmations ({compositeScore.ConfirmationCount} < {MIN_CONFIRMATIONS})");
					finalReason = $"Only {compositeScore.ConfirmationCount} confirmations";
				}
				// ⭐ NEW: Primary filters must pass
				else if (compositeScore != null && !compositeScore.MeetsPrimaryFilters)
				{
					Console.WriteLine($"❌ Primary filters not met");
					finalReason = "Primary filters failed";
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

						// ⭐ NEW: Use composite score as final confidence
						// Apply Bollinger conflict penalty if detected
						decimal bollingerPenalty = bollingerConflict ? BOLLINGER_CONFLICT_PENALTY : 0m;
						finalConfidence = compositeScore != null
							? Math.Min(compositeScore.TotalScore + mtfBonus + primaryConfidence * 0.1m - bollingerPenalty, 1.0m)
							: Math.Min(avgBuyConfidence + mtfBonus - bollingerPenalty, 1.0m);

						finalReason = compositeScore != null
							? $"✅ BUY: {buyVotes} votes, Composite={compositeScore.TotalScore:P0}, Confirms={compositeScore.ConfirmationCount}{(bollingerConflict ? " (BB conflict)" : "")}"
							: $"Buy: {buyVotes} votes @ {avgBuyConfidence:P0}, Q:{qualityScore:P0}";

						Console.WriteLine($"\n   ═══════════════════════════════════════");
						Console.WriteLine($"   ✅ SIGNAL APPROVED: BUY");
						Console.WriteLine($"   ═══════════════════════════════════════");
						Console.WriteLine($"   Confidence: {finalConfidence:P0}");
						Console.WriteLine($"   Votes: {buyVotes} Buy vs {sellVotes} Sell");
						Console.WriteLine($"   Quality: {qualityScore:P0}");
						if (compositeScore != null)
						{
							Console.WriteLine($"   Composite: {compositeScore.TotalScore:P0}");
							Console.WriteLine($"   Confirmations: {compositeScore.ConfirmationCount}");
						}
						Console.WriteLine($"   ═══════════════════════════════════════");
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
						// Apply Bollinger conflict penalty if detected
						decimal bollingerPenalty = bollingerConflict ? BOLLINGER_CONFLICT_PENALTY : 0m;
						finalConfidence = Math.Min(avgSellConfidence + mtfBonus - bollingerPenalty, 0.95m);
						finalReason = $"Sell: {sellVotes}@{avgSellConfidence:P0} Q:{qualityScore:P0}{(bollingerConflict ? " (BB conflict)" : "")}";
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
					? entry + (stopDistance * 2.0m * trendMultiplier)
					: entry - (stopDistance * 2.0m * trendMultiplier);

				decimal profitTarget2 = finalSignal == "Buy"
					? entry + (stopDistance * 3.0m * trendMultiplier)
					: entry - (stopDistance * 3.0m * trendMultiplier);

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
				_db.InsertSignal(symbol, lastBarDate, "RelaxedEngine", "Hold", finalReason);
			}

			// Detailed logging
			LogEnhancedSignals(symbol, new Dictionary<string, StrategySignal>
			{
				{ "TrendMTF", s1 }, { "MeanRevSR", s2 }, { "BreakoutVol", s3 },
				{ "MomentumDiv", s4 }, { "EMA+RSI", s5 }, { "Bollinger", s6 },
				{ "ATR", s7 }, { "MACD", s8 }, { "ADX", s9 }, { "Volume", s10 },
				{ "Donchian", s11 }, 
				//{ "VWAP", s12 }, 
				{ "Ichimoku", s13 },
				{ "PriceAction", s14 },
				{ "Supertrend", s15 }, { "MeanRevMFI", s16 }, { "TripleMomentum", s17 },
				{ "SRBounce", s18 }, 
				//{ "GapTrading", s19 }, 
				{ "CMFMomentum", s20 },
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

				_db.InsertTrade(symbol, DateTime.UtcNow, lastBarDate,finalSignal, (long)qty, entry, (long)qty* entry, finalConfidence, qualityScore);
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

			// RELAXED validation checks - generate more signals

			// 1. EMA separation - relaxed threshold
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);
			decimal emaSeparation = Math.Abs(ema20[idx] - ema50[idx]) / ema50[idx];

			// RELAXED: 0.4% EMA separation required
			if (emaSeparation < 0.004m)
			{
				Console.WriteLine($"   ❌ EMA too close ({emaSeparation:P2} < 0.4%)");
				return false;
			}

			// 2. RSI - RELAXED thresholds but still avoid extremes
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiVal = rsi[^1];
				// RELAXED: RSI 72 for overbought
				if (direction == "Buy" && rsiVal > 72m)
				{
					Console.WriteLine($"   ❌ RSI overbought ({rsiVal:F0} > 78)");
					return false;
				}
				// RELAXED: RSI 22 for oversold
				if (direction == "Sell" && rsiVal < 22m)
				{
					Console.WriteLine($"   ❌ RSI oversold ({rsiVal:F0} < 22)");
					return false;
				}
			}

			// 3. Recent momentum - RELAXED
			if (idx >= 7)
			{
				decimal price7DaysAgo = closes[idx - 7];
				decimal momentum7D = (closes[idx] - price7DaysAgo) / price7DaysAgo;

				bool momentumMatch = (direction == "Buy" && momentum7D > 0) ||
									(direction == "Sell" && momentum7D < 0);

				// RELAXED: 0.5% minimum momentum
				if (!momentumMatch || Math.Abs(momentum7D) < 0.005m)
				{
					Console.WriteLine($"   ❌ Weak momentum ({momentum7D:P2} < 0.5%)");
					return false;
				}
			}

			// 4. Volume health check - relaxed
			if (volumes.Count > 20 && idx >= 20)
			{
				var avg5 = volumes.Skip(idx - 5).Take(5).Average();
				var avg20 = volumes.Skip(idx - 20).Take(20).Average();

				// RELAXED: 40% of average volume required
				if (avg5 < avg20 * 0.40m)
				{
					Console.WriteLine($"   ❌ Volume declining ({avg5 / avg20:P0} < 40%)");
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

		/// <summary>
		/// Calculate comprehensive composite signal score
		/// Returns detailed breakdown of signal quality
		/// </summary>
		private SignalValidator.CompositeSignalScore CalculateCompositeSignalScore(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string direction,
			int idx)
		{
			// Calculate all required indicators
			var (adx, diPlus, diMinus) = Indicators.ADXList(highs, lows, closes, 14);
			var choppiness = Indicators.ChoppinessIndex(highs, lows, closes, 14);
			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = closes.Count >= 200 ? Indicators.EMAList(closes, 200) : ema50;
			var emaFast = Indicators.EMAList(closes, 20);
			var emaSlow = Indicators.EMAList(closes, 50);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, signal, hist) = Indicators.MACDSeries(closes);
			var (supertrend, stDir) = Indicators.SupertrendList(highs, lows, closes, 14, 2.5m);
			var atr = Indicators.ATRList(highs, lows, closes, 14);

			// PRIMARY FILTERS
			var trendStrength = SignalValidator.ValidateTrendStrength(
				adx, diPlus, diMinus, idx, direction);

			var choppyFilter = SignalValidator.ValidateChoppinessFilter(
				choppiness, idx);

			var trendAlign = SignalValidator.ValidateTrendAlignment(
				closes, ema50, ema200, idx, direction);

			// ENTRY SIGNALS
			var emaValid = SignalValidator.ValidateEMACrossover(
				closes, emaFast, emaSlow, idx, direction);

			var rsiValid = SignalValidator.ValidateRSIWithDivergence(
				rsiList, closes, idx, direction);

			var macdValid = SignalValidator.ValidateMACDEnhanced(
				macd, signal, hist, closes, idx, direction);

			var stValid = SignalValidator.ValidateSupertrendSwing(
				supertrend, stDir, closes, atr, idx);

			// CONFIRMATION
			var volumeValid = SignalValidator.ValidateVolumeSpikeSwing(
				volumes, closes, idx, 1.5m);

			// Calculate composite score using the new method
			return SignalValidator.CalculateCompositeScore(
				trendStrength, choppyFilter, trendAlign,
				emaValid, rsiValid, macdValid, stValid, volumeValid);
		}
		private StrategySignal Hold(string reason) => new("Hold", 0m, reason);


		public async System.Threading.Tasks.Task SendSessionNotificationsAsync(List<string> recipientEmail)
		{
			if (_emailService == null)
			{
				Console.WriteLine("⚠️ Email service not configured");
				return;
			}
			// ⭐ BALANCED: Send signals with 60%+ confidence
			var buySignals = _sessionSignals.Where(s => s.Direction == "Buy" && s.Confidence >= .7m && s.Quality >=.7m).ToList();

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


		public void ClearSessionSignals()
		{
			while (_sessionSignals.TryTake(out _)) { }
		}

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