using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	/// <summary>
	/// SWING TRADING OPTIMIZED Trade Engine
	/// All thresholds and logic tuned for 1-2 week holding periods on DAILY bars
	/// 
	/// Key Changes from Original:
	/// - Wider stop losses (3-8% vs 2-5%)
	/// - Higher quality thresholds (55% vs 52%)
	/// - More consensus required (7 votes vs 6)
	/// - Better validation for swing trades
	/// - Adjusted profit targets (2.5:1 and 4:1)
	/// </summary>
	public class TradeEngineEnhanced
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;
		private readonly EmailNotificationService? _emailService;
		private readonly List<TradingSignal> _sessionSignals;

		// ⭐ SWING TRADING THRESHOLDS - OPTIMIZED
		private const int MIN_VOTES_REQUIRED = 7;              // ⭐ 7 strategies needed
		private const decimal MIN_STRATEGY_CONFIDENCE = 0.50m; // ⭐ 50% individual confidence
		private const decimal MIN_FINAL_CONFIDENCE = 0.58m;    // ⭐ 58% average confidence
		private const decimal MIN_QUALITY_SCORE = 0.55m;       // ⭐ 55% quality threshold
		private const int MIN_STRATEGIES_FOR_ENTRY = 7;        // ⭐ 7 minimum strategies

		public TradeEngineEnhanced(SqliteStorage db, decimal riskPercent = 0.015m,
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
			if (closes.Count < 120)
			{
				Console.WriteLine($"⚠️ Insufficient data for {symbol} (need 120+ daily bars)");
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
			// STEP 1: MARKET REGIME ANALYSIS
			// ═══════════════════════════════════════════════════════════════

			var regime = Indicators.DetectMarketRegime(closes, highs, lows);

			Console.WriteLine($"\n📊 {symbol} Market Analysis:");
			Console.WriteLine($"   Regime: {regime.Description} (confidence: {regime.RegimeConfidence:P0})");
			Console.WriteLine($"   Trend Strength: {regime.TrendStrength:P2}");
			Console.WriteLine($"   Volatility: {regime.VolatilityLevel:P2}");

			// ⭐ SWING: Skip extreme volatility
			if (regime.VolatilityLevel > 0.08m)
			{
				Console.WriteLine($"⚠️ EXTREME volatility ({regime.VolatilityLevel:P2}) - skipping");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Extreme volatility");
				return;
			}

			// ⭐ SWING: Skip very low volatility
			if (regime.VolatilityLevel < 0.008m)
			{
				Console.WriteLine($"⚠️ Very low volatility ({regime.VolatilityLevel:P2}) - not suitable for swing");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Regime", "Hold", "Insufficient volatility");
				return;
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 2: MULTI-TIMEFRAME CONFIRMATION
			// ═══════════════════════════════════════════════════════════════

			var mtf = Indicators.AnalyzeMultiTimeframe(closes, highs, lows);
			Console.WriteLine($"   MTF Analysis: {mtf.Reason}");
			Console.WriteLine($"   MTF Confidence: {mtf.Confidence:P0}");

			// ═══════════════════════════════════════════════════════════════
			// STEP 3: CALCULATE SWING-OPTIMIZED INDICATORS
			// ═══════════════════════════════════════════════════════════════

			// ⭐ SWING: Use 20/50 EMAs for swing trading
			var emaShort = Indicators.EMAList(closes, 20);
			var emaLong = Indicators.EMAList(closes, 50);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 25, 2);

			// ⭐ SWING: Use 20-period ATR for weekly volatility
			var atr = Indicators.ATRList(highs, lows, closes, 20);

			// ═══════════════════════════════════════════════════════════════
			// STEP 4: EXECUTE ALL STRATEGIES
			// ═══════════════════════════════════════════════════════════════

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

			var s5 = Strategies.EmaRsi(closes, 20, 50, 14);
			var s6 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s7 = Strategies.AtrBreakout(closes, highs, lows, atr, 20, 50, 14);
			var s8 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

			// ⭐ SWING: Reduced ADX threshold to 22
			var s9 = Strategies.AdxFilter(highs, lows, closes, 14, 22m);

			// ⭐ SWING: Relaxed volume confirmation (1.0x vs 1.2x)
			var s10 = volumes != null ?
				Strategies.VolumeConfirm(closes, volumes, 30, 1.0m) :
				Hold("No volume");

			var s11 = Strategies.DonchianBreakout(highs, lows, closes, 25);

			var s12 = volumes != null ?
				Strategies.VWAPStrategy(closes, highs, lows, volumes) :
				Hold("No volume");
			var s13 = Strategies.IchimokuCloud(closes, highs, lows);
			var s14 = Strategies.PriceActionTrend(closes, highs, lows);

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

			var s31 = volumes != null ?
				Strategies.VolatilitySqueeze(closes, highs, lows, volumes) :
				Hold("No volume");

			var s32 = Strategies.ElderTripleScreen(closes, highs, lows);

			var s33 = volumes != null ?
				Strategies.ElderRayStrategy(highs, lows, closes, volumes) :
				Hold("No volume");

			var s34 = Strategies.ChoppinessFilter(highs, lows, closes);
			var s35 = Strategies.WeeklyTrendFilter(closes, highs, lows);

			var s36 = volumes != null ?
				Strategies.LinearRegressionBreakout(closes, volumes) :
				Hold("No volume");

			var s37 = opens != null ?
				Strategies.HeikinAshiTrend(opens, highs, lows, closes) :
				Hold("No open data");

			var s38 = Strategies.FibonacciRetracement(highs, lows, closes);

			var allSignals = new List<(StrategySignal signal, int index)> {
				(s1,0), (s2,1), (s3,2), (s4,3), (s5,4), (s6,5), (s7,6), (s8,7), (s9,8), (s10,9),
				(s11,10), (s12,11), (s13,12), (s14,13), (s15,14), (s16,15), (s17,16), (s18,17),
				(s19,18), (s20,19),(s21,20), (s22,21), (s23,22), (s24,23), (s25,24), (s26,25),
				(s27,26), (s28,27), (s29,28), (s30,29),
				(s31,30), (s32,31), (s33,32), (s34,33), (s35,34), (s36,35), (s37,36), (s38,37)
			};

			// ═══════════════════════════════════════════════════════════════
			// STEP 5: VOTE COUNTING WITH WEIGHTED CONFIDENCE
			// ═══════════════════════════════════════════════════════════════

			// ⭐ SWING: Higher confidence threshold (50% vs 48%)
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
					// Enhanced strategies get 1.2x weight
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
			Console.WriteLine($"   Required: {MIN_VOTES_REQUIRED} votes @ {MIN_FINAL_CONFIDENCE:P0}");

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
				// ⭐ SWING: Use swing-optimized quality score
				qualityScore = ImprovedQualityScore.CalculateSwingQualityScore(
					opens, closes, highs, lows, volumes, preliminaryDirection);

				Console.WriteLine($"   Swing Quality Score: {qualityScore:P0}");
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 7: FINAL DECISION WITH SWING VALIDATION
			// ═══════════════════════════════════════════════════════════════

			string finalSignal = "Hold";
			decimal finalConfidence = 0m;
			string finalReason = "No consensus";

			decimal mtfBonus = 0m;
			bool mtfAligned = false;

			// ⭐ SWING: Stricter thresholds (7 votes, 58% confidence, 55% quality)
			if (buyVotes >= MIN_VOTES_REQUIRED && avgBuyConfidence >= MIN_FINAL_CONFIDENCE)
			{
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Up")
				{
					mtfAligned = true;
					mtfBonus = 0.08m; // 8% bonus for MTF alignment
				}

				if (qualityScore >= MIN_QUALITY_SCORE)
				{
					// ⭐ SWING: Enhanced validation for swing trades
					bool isValid = ValidateSwingSignal(
						"Buy", buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence,
						qualityScore, closes, highs, lows, volumes ?? new List<decimal>());

					bool hasCoreConfirmation = HasCoreStrategyConfirmation(buySignals, "Buy");

					if (!hasCoreConfirmation)
					{
						finalReason = "Need core strategy confirmation";
						Console.WriteLine($"   ❌ Rejected: No core strategy confirmation");
					}
					else if (isValid)
					{
						finalSignal = "Buy";
						finalConfidence = Math.Min(avgBuyConfidence + mtfBonus, 1.0m);
						finalReason = $"Buy consensus: {buyVotes} strategies @ {avgBuyConfidence:P0}";
						if (mtfAligned) finalReason += " + MTF aligned";
						if (qualityScore >= 0.65m) finalReason += " + excellent quality";
					}
					else
					{
						finalReason = "Buy signal failed swing validation";
					}
				}
				else
				{
					finalReason = $"Quality too low: {qualityScore:P0} (need {MIN_QUALITY_SCORE:P0})";
				}
			}
			else if (sellVotes >= MIN_VOTES_REQUIRED && avgSellConfidence >= MIN_FINAL_CONFIDENCE)
			{
				if (mtf.IsAligned && mtf.CurrentTFTrend == "Down")
				{
					mtfAligned = true;
					mtfBonus = 0.08m;
				}

				if (qualityScore >= MIN_QUALITY_SCORE)
				{
					bool isValid = ValidateSwingSignal(
						"Sell", buyVotes, sellVotes, avgBuyConfidence, avgSellConfidence,
						qualityScore, closes, highs, lows, volumes ?? new List<decimal>());

					bool hasCoreConfirmation = HasCoreStrategyConfirmation(sellSignals, "Sell");

					if (!hasCoreConfirmation)
					{
						finalReason = "Need core strategy confirmation";
						Console.WriteLine($"   ❌ Rejected: No core strategy confirmation");
					}
					else if (isValid)
					{
						finalSignal = "Sell";
						finalConfidence = Math.Min(avgSellConfidence + mtfBonus, 0.95m);
						finalReason = $"Sell consensus: {sellVotes} strategies @ {avgSellConfidence:P0}";
						if (mtfAligned) finalReason += " + MTF";
					}
					else
					{
						finalReason = "Sell signal failed swing validation";
					}
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
					finalReason = $"Confidence too low (need {MIN_FINAL_CONFIDENCE:P0})";
				}
				else
				{
					finalReason = $"Insufficient votes (need {MIN_VOTES_REQUIRED})";
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// STEP 8: SWING TRADING POSITION SIZING & RISK MANAGEMENT
			// ═══════════════════════════════════════════════════════════════

			decimal lastClose = closes[idx];
			decimal atrVal = atr.LastOrDefault();
			decimal entry = lastClose;

			// ⭐ SWING: Wider base stop calculation (3x ATR vs 2.5x)
			decimal stopDistance = atrVal > 0 ? atrVal * 3.0m : lastClose * 0.04m;

			if (finalSignal != "Hold")
			{
				var srLevels = Indicators.FindSupportResistance(highs, lows, closes);

				if (finalSignal == "Buy")
				{
					entry = Math.Round(lastClose * 1.001m, 2);

					// ⭐ SWING: Find support for stop placement
					var nearestSupport = srLevels
						.Where(l => l.IsSupport && l.Level < lastClose)
						.OrderByDescending(l => l.Level)
						.FirstOrDefault();

					if (nearestSupport != null)
					{
						decimal srStop = lastClose - nearestSupport.Level;
						// ⭐ SWING: Accept stops between 2% and 8%
						if (srStop > lastClose * 0.02m && srStop < lastClose * 0.08m)
						{
							stopDistance = srStop;
						}
					}

					// ⭐ SWING: Minimum 3% stop, maximum 8% (vs 2-5%)
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
						{
							stopDistance = srStop;
						}
					}

					stopDistance = Math.Max(stopDistance, lastClose * 0.03m);
					stopDistance = Math.Min(stopDistance, lastClose * 0.08m);
				}
			}

			// ⭐ SWING: Position sizing with quality adjustment
			decimal equity = 10000m;
			decimal riskValue = equity * _riskPercent;

			// Quality-based risk adjustment
			decimal adjustedRisk = riskValue * Math.Max(qualityScore * 1.4m, 0.7m);
			decimal qty = Math.Max(1, Math.Floor(adjustedRisk / stopDistance));

			// ═══════════════════════════════════════════════════════════════
			// STEP 9: SWING PROFIT TARGETS & EXITS
			// ═══════════════════════════════════════════════════════════════

			if (finalSignal != "Hold")
			{
				var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

				// ⭐ SWING: Scale targets with trend strength
				decimal trendMultiplier = context.TrendStrength > 0.02m ? 1.2m : 1.0m;

				// ⭐ SWING: Better profit targets (2.5:1 and 4:1 vs 2:1 and 3:1)
				decimal profitTarget1 = finalSignal == "Buy"
					? entry + (stopDistance * 2.5m * trendMultiplier)
					: entry - (stopDistance * 2.5m * trendMultiplier);

				decimal profitTarget2 = finalSignal == "Buy"
					? entry + (stopDistance * 4.0m * trendMultiplier)
					: entry - (stopDistance * 4.0m * trendMultiplier);

				decimal stopLoss = finalSignal == "Buy"
					? entry - stopDistance
					: entry + stopDistance;

				// ⭐ SWING: Calculate max hold period (7-15 days)
				int maxHoldDays = CalculateMaxHoldPeriod(context.TrendStrength, qualityScore);

				Console.WriteLine($"\n✅ {finalSignal} SIGNAL for {symbol}");
				Console.WriteLine($"   Confidence: {finalConfidence:P0} | Quality: {qualityScore:P0}");
				Console.WriteLine($"   Entry: ${entry:F2} | Qty: {qty}");
				Console.WriteLine($"   Stop: ${stopLoss:F2} ({(stopDistance / entry * 100):F1}%)");
				Console.WriteLine($"   Target 1: ${profitTarget1:F2} ({(stopDistance * 2.5m / entry * 100):F1}%)");
				Console.WriteLine($"   Target 2: ${profitTarget2:F2} ({(stopDistance * 4.0m / entry * 100):F1}%)");
				Console.WriteLine($"   Max Hold: {maxHoldDays} trading days");
				Console.WriteLine($"   Reason: {finalReason}");

				// Store in database
				_db.InsertSignal(symbol, lastBarDate, "Consensus", finalSignal, finalReason);

				//create trade record
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
					{ "VolSqueeze", s31 }, { "TripleScreen", s32 }, { "ElderRay", s33 },
					{ "Choppiness", s34 }, { "WeeklyFilter", s35 }, { "LinRegress", s36 },
					{ "HeikinAshi", s37 }, { "Fibonacci", s38 }
				});

				_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Consensus", finalSignal,
					$"{finalReason} | conf={finalConfidence:P0} | quality={qualityScore:P0}");

				if (finalSignal != "Hold")
				{
					_db.InsertSignal(symbol, DateTime.UtcNow, "Enhanced_Entry", finalSignal,
						$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}, quality={qualityScore:P0}");

					_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, (long)qty, entry, finalConfidence, qualityScore, lastBarDate);
				}

				// ═

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
				Console.WriteLine($"\n⏸️  HOLD for {symbol}");
				Console.WriteLine($"   Reason: {finalReason}");
				_db.InsertSignal(symbol, lastBarDate, "Consensus", "Hold", finalReason);
			}
		}

		/// <summary>
		/// ⭐ SWING: Enhanced validation specifically for swing trades
		/// Checks weekly patterns, volatility, volume consistency, and trend alignment
		/// </summary>
		private bool ValidateSwingSignal(
			string direction,
			int buyVotes,
			int sellVotes,
			decimal buyConfidence,
			decimal sellConfidence,
			decimal quality,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			int idx = closes.Count - 1;

			// 1. Check vote dominance (60% majority)
			int totalVotes = buyVotes + sellVotes;
			int directionVotes = direction == "Buy" ? buyVotes : sellVotes;
			decimal voteRatio = (decimal)directionVotes / totalVotes;

			if (voteRatio < 0.60m)
			{
				Console.WriteLine($"   ⚠️ Validation: Vote ratio {voteRatio:P0} < 60%");
				return false;
			}

			// 2. ⭐ SWING: Check 20/50 EMA trend alignment
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			if (direction == "Buy" && ema20[idx] < ema50[idx] * 0.995m)
			{
				// Allow counter-trend ONLY if RSI shows extreme oversold
				var rsi = Indicators.RSIList(closes, 14);
				if (rsi.Count == 0 || rsi[^1] > 30m)
				{
					Console.WriteLine($"   ⚠️ Validation: Buy in downtrend without oversold RSI");
					return false;
				}
			}

			// 3. ⭐ SWING: Check weekly range (7-day volatility)
			if (idx >= 7)
			{
				var recent7 = closes.Skip(idx - 7).Take(7).ToList();
				decimal highLow = (recent7.Max() - recent7.Min()) / recent7.Average();

				// Must have at least 3% range over week
				if (highLow < 0.03m)
				{
					Console.WriteLine($"   ⚠️ Validation: Insufficient weekly range {highLow:P1}");
					return false;
				}
			}

			// 4. ⭐ SWING: Check volume consistency (5-day vs 30-day)
			if (volumes.Count > 30 && idx >= 30)
			{
				var avg5 = volumes.Skip(idx - 5).Take(5).Average();
				var avg30 = volumes.Skip(idx - 30).Take(30).Average();

				if (avg5 < avg30 * 0.6m)
				{
					Console.WriteLine($"   ⚠️ Validation: Low recent volume {avg5 / avg30:P0}");
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// ⭐ SWING: Calculate maximum hold period based on trend and quality
		/// Returns number of trading days before re-evaluation
		/// </summary>
		private int CalculateMaxHoldPeriod(decimal trendStrength, decimal quality)
		{
			// Base: 10 trading days (2 weeks)
			int baseDays = 10;

			// Strong trend + high quality = hold longer
			if (trendStrength > 0.02m && quality > 0.65m)
				return 15;  // 3 weeks

			// Weak trend = shorter hold
			if (trendStrength < 0.01m || quality < 0.55m)
				return 7;   // 1.5 weeks

			return baseDays;
		}

		/// <summary>
		/// Check if at least one core strategy (0-8) is voting
		/// Prevents new/experimental strategies from generating signals alone
		/// </summary>
		private bool HasCoreStrategyConfirmation(List<(StrategySignal signal, int index)> signals, string direction)
		{
			// Core strategies are indices 0-8
			return signals.Any(s => s.index <= 8 && s.signal.Signal == direction);
		}

		private StrategySignal Hold(string reason) => new("Hold", 0m, reason);
		public async System.Threading.Tasks.Task SendSessionNotificationsAsync(string recipientEmail)
		{
			if (_emailService == null)
			{
				Console.WriteLine("⚠️ Email service not configured");
				return;
			}
			// ⭐ SWING: Only send high-confidence swing trades (70%+)
			var buySignals = _sessionSignals.Where(s => s.Direction == "Buy" && s.Confidence > .7m).ToList();

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
	}

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
}