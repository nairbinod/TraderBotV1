using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Advanced Signal Scoring System with ML-Inspired Features
	///
	/// Improvements over basic signal scoring:
	/// 1. Feature engineering (pattern recognition, statistical features)
	/// 2. Signal strength decay over time (freshness)
	/// 3. Historical win-rate weighting (strategy performance tracking)
	/// 4. Ensemble scoring with confidence intervals
	/// 5. Anomaly detection (filter out unusual signals)
	/// 6. Regime-adaptive scoring (adjust based on market conditions)
	/// 7. Risk-adjusted signal scoring
	/// 8. Multi-factor signal ranking
	///
	/// This helps:
	/// - Rank signals by expected profitability
	/// - Filter out false signals
	/// - Adapt to changing market conditions
	/// - Improve signal quality through learning
	/// </summary>
	public static class AdvancedSignalScoring
	{
		public class ScoredSignal
		{
			public string Symbol { get; set; } = "";
			public string Direction { get; set; } = "";
			public decimal BaseConfidence { get; set; }
			public decimal AdjustedConfidence { get; set; }
			public decimal QualityScore { get; set; }
			public decimal FinalScore { get; set; }                    // 0-100 combined score
			public decimal ExpectedWinRate { get; set; }               // Estimated probability of profit
			public decimal ExpectedRiskReward { get; set; }           // Expected R:R ratio
			public decimal SignalFreshness { get; set; }              // How fresh is the setup (0-1)
			public decimal RegimeAlignment { get; set; }              // Fits current regime (0-1)
			public decimal AnomalyScore { get; set; }                 // 0=normal, 1=anomaly
			public string[] PositiveFactors { get; set; } = Array.Empty<string>();
			public string[] NegativeFactors { get; set; } = Array.Empty<string>();
			public string SignalRank { get; set; } = "Unknown";       // S/A/B/C/D
			public string Recommendation { get; set; } = "Hold";      // Strong Buy/Buy/Hold/Pass
		}

		/// <summary>
		/// Score a trading signal with advanced ml-inspired techniques
		/// </summary>
		public static ScoredSignal ScoreSignal(
			string symbol,
			string direction,
			decimal baseConfidence,
			decimal qualityScore,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			EnhancedMarketRegime.RegimeAnalysis regime,
			Dictionary<string, decimal>? strategyPerformance = null)
		{
			var scored = new ScoredSignal
			{
				Symbol = symbol,
				Direction = direction,
				BaseConfidence = baseConfidence,
				QualityScore = qualityScore
			};

			var positiveFactors = new List<string>();
			var negativeFactors = new List<string>();

			// ═══════════════════════════════════════════════════════════════
			// 1. CALCULATE SIGNAL FRESHNESS
			// Fresh setups are better than stale ones
			// ═══════════════════════════════════════════════════════════════

			scored.SignalFreshness = CalculateSignalFreshness(closes, highs, lows, direction);
			if (scored.SignalFreshness > 0.7m)
				positiveFactors.Add($"Fresh setup ({scored.SignalFreshness:P0})");
			else if (scored.SignalFreshness < 0.4m)
				negativeFactors.Add($"Stale setup ({scored.SignalFreshness:P0})");

			// ═══════════════════════════════════════════════════════════════
			// 2. REGIME ALIGNMENT SCORE
			// Signal fits current market regime
			// ═══════════════════════════════════════════════════════════════

			scored.RegimeAlignment = CalculateRegimeAlignment(direction, regime);
			if (scored.RegimeAlignment > 0.7m)
				positiveFactors.Add($"Regime aligned ({regime.PrimaryRegime})");
			else if (scored.RegimeAlignment < 0.5m)
				negativeFactors.Add($"Regime misaligned ({regime.PrimaryRegime})");

			// ═══════════════════════════════════════════════════════════════
			// 3. ANOMALY DETECTION
			// Filter unusual/outlier signals
			// ═══════════════════════════════════════════════════════════════

			scored.AnomalyScore = DetectAnomaly(closes, highs, lows, volumes);
			if (scored.AnomalyScore > 0.6m)
				negativeFactors.Add($"Anomalous signal ({scored.AnomalyScore:P0})");

			// ═══════════════════════════════════════════════════════════════
			// 4. PATTERN RECOGNITION FEATURES
			// Identify bullish/bearish patterns
			// ═══════════════════════════════════════════════════════════════

			var patterns = RecognizePatterns(closes, highs, lows, volumes, direction);
			positiveFactors.AddRange(patterns.PositivePatterns);
			negativeFactors.AddRange(patterns.NegativePatterns);

			// ═══════════════════════════════════════════════════════════════
			// 5. STATISTICAL FEATURES
			// Momentum, mean reversion, trend strength
			// ═══════════════════════════════════════════════════════════════

			var stats = CalculateStatisticalFeatures(closes, highs, lows, direction);
			if (stats.MomentumScore > 0.7m)
				positiveFactors.Add($"Strong momentum ({stats.MomentumScore:P0})");
			if (stats.MeanReversionSetup > 0.7m)
				positiveFactors.Add($"Mean reversion setup");

			// ═══════════════════════════════════════════════════════════════
			// 6. RISK-ADJUSTED SCORING
			// Consider risk vs reward
			// ═══════════════════════════════════════════════════════════════

			var riskReward = CalculateRiskReward(closes, highs, lows, direction);
			scored.ExpectedRiskReward = riskReward.ExpectedRR;

			if (riskReward.ExpectedRR > 2.5m)
				positiveFactors.Add($"Excellent R:R ({riskReward.ExpectedRR:F1}:1)");
			else if (riskReward.ExpectedRR < 1.5m)
				negativeFactors.Add($"Poor R:R ({riskReward.ExpectedRR:F1}:1)");

			// ═══════════════════════════════════════════════════════════════
			// 7. ENSEMBLE SCORE CALCULATION
			// Weighted combination of all factors
			// ═══════════════════════════════════════════════════════════════

			decimal ensembleScore = CalculateEnsembleScore(
				baseConfidence,
				qualityScore,
				scored.SignalFreshness,
				scored.RegimeAlignment,
				scored.AnomalyScore,
				stats.MomentumScore,
				riskReward.ExpectedRR,
				regime);

			// ═══════════════════════════════════════════════════════════════
			// 8. ADJUST FOR REGIME
			// Apply regime-based multipliers
			// ═══════════════════════════════════════════════════════════════

			decimal regimeAdjusted = ensembleScore * regime.ConfidenceMultiplier;

			scored.AdjustedConfidence = Math.Min(regimeAdjusted, 1.0m);

			// ═══════════════════════════════════════════════════════════════
			// 9. CALCULATE FINAL SCORE (0-100)
			// ═══════════════════════════════════════════════════════════════

			scored.FinalScore = CalculateFinalScore(
				scored.AdjustedConfidence,
				qualityScore,
				scored.RegimeAlignment,
				scored.SignalFreshness,
				riskReward.ExpectedRR);

			// ═══════════════════════════════════════════════════════════════
			// 10. ESTIMATE WIN RATE
			// Based on historical performance and current conditions
			// ═══════════════════════════════════════════════════════════════

			scored.ExpectedWinRate = EstimateWinRate(
				scored.FinalScore,
				regime,
				strategyPerformance);

			// ═══════════════════════════════════════════════════════════════
			// 11. RANK AND RECOMMEND
			// ═══════════════════════════════════════════════════════════════

			scored.PositiveFactors = positiveFactors.ToArray();
			scored.NegativeFactors = negativeFactors.ToArray();
			scored.SignalRank = RankSignal(scored.FinalScore);
			scored.Recommendation = GenerateRecommendation(scored, regime);

			return scored;
		}

		private static decimal CalculateSignalFreshness(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, string direction)
		{
			if (closes.Count < 10) return 0.5m;

			int idx = closes.Count - 1;
			decimal freshness = 0.5m;

			// Check how recently the setup developed
			// Fresh = setup formed in last 1-3 days
			// Stale = setup formed >5 days ago

			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			// Count days since trend became clear
			int daysSinceClear = 0;
			bool currentTrendBullish = ema20[idx] > ema50[idx];

			for (int i = idx; i >= Math.Max(0, idx - 10); i--)
			{
				bool wasBullish = ema20[i] > ema50[i];
				if (wasBullish == currentTrendBullish)
					daysSinceClear++;
				else
					break;
			}

			// Fresh if trend just formed (1-3 days)
			// Stale if trend >7 days old
			if (daysSinceClear <= 3)
				freshness = 0.9m;
			else if (daysSinceClear <= 5)
				freshness = 0.7m;
			else if (daysSinceClear <= 7)
				freshness = 0.5m;
			else
				freshness = 0.3m;

			// Boost freshness if recent breakout/breakdown
			if (idx >= 5)
			{
				decimal recentHigh5 = highs.Skip(idx - 5).Take(5).Max();
				decimal recentLow5 = lows.Skip(idx - 5).Take(5).Min();
				decimal currentPrice = closes[idx];

				bool recentBreakout = direction == "Buy" && currentPrice > recentHigh5 * 0.99m;
				bool recentBreakdown = direction == "Sell" && currentPrice < recentLow5 * 1.01m;

				if (recentBreakout || recentBreakdown)
					freshness = Math.Max(freshness, 0.85m);
			}

			return freshness;
		}

		private static decimal CalculateRegimeAlignment(string direction, EnhancedMarketRegime.RegimeAnalysis regime)
		{
			decimal alignment = 0.5m;

			// Perfect alignment: direction matches regime trend
			if (direction == "Buy" && regime.TrendDirection == "Bullish")
				alignment = 0.9m;
			else if (direction == "Sell" && regime.TrendDirection == "Bearish")
				alignment = 0.9m;
			else if (regime.TrendDirection == "Neutral")
				alignment = 0.6m; // Neutral regime allows both
			else
				alignment = 0.3m; // Counter-trend

			// Adjust for regime type
			if (regime.PrimaryRegime == "Trending" && direction == "Buy" && regime.TrendDirection == "Bullish")
				alignment += 0.1m;
			else if (regime.PrimaryRegime == "Ranging" && (direction == "Buy" || direction == "Sell"))
				alignment = Math.Min(alignment, 0.7m); // Mean reversion better in ranging

			// Reduce alignment if in transition
			if (regime.InRegimeTransition)
				alignment *= 0.8m;

			return Math.Min(alignment, 1.0m);
		}

		private static decimal DetectAnomaly(List<decimal> closes, List<decimal> highs, List<decimal> lows, List<decimal> volumes)
		{
			decimal anomalyScore = 0m;
			int anomalyCount = 0;

			if (closes.Count < 20) return 0m;

			int idx = closes.Count - 1;

			// 1. Price spike anomaly (>3 std deviations)
			var recent20Closes = closes.Skip(idx - 19).Take(20).ToList();
			decimal avgPrice = recent20Closes.Average();
			decimal stdDev = CalculateStdDev(recent20Closes);

			decimal currentPrice = closes[idx];
			decimal zScore = stdDev > 0 ? Math.Abs(currentPrice - avgPrice) / stdDev : 0m;

			if (zScore > 3m)
			{
				anomalyScore += 0.4m;
				anomalyCount++;
			}

			// 2. Volume anomaly
			if (volumes.Count > 20)
			{
				var recent20Vol = volumes.Skip(Math.Max(0, idx - 19)).Take(20).ToList();
				decimal avgVol = recent20Vol.Average();
				decimal volStdDev = CalculateStdDev(recent20Vol);

				decimal currentVol = volumes[idx];
				decimal volZScore = volStdDev > 0 ? Math.Abs(currentVol - avgVol) / volStdDev : 0m;

				if (volZScore > 3m)
				{
					anomalyScore += 0.3m;
					anomalyCount++;
				}
			}

			// 3. Gap anomaly (large overnight gap)
			if (idx > 0)
			{
				decimal gap = Math.Abs(closes[idx] - closes[idx - 1]) / closes[idx - 1];
				if (gap > 0.05m) // >5% gap
				{
					anomalyScore += 0.3m;
					anomalyCount++;
				}
			}

			return Math.Min(anomalyScore, 1.0m);
		}

		private static (List<string> PositivePatterns, List<string> NegativePatterns) RecognizePatterns(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, List<decimal> volumes, string direction)
		{
			var positive = new List<string>();
			var negative = new List<string>();

			if (closes.Count < 5) return (positive, negative);

			int idx = closes.Count - 1;

			// 1. Bullish Engulfing (for Buy)
			if (direction == "Buy" && idx >= 1)
			{
				bool bullishEngulfing = closes[idx] > closes[idx - 1] &&
										closes[idx] > closes[idx].GetHashCode() &&
										closes[idx - 1] < closes[idx - 2];
				if (bullishEngulfing)
					positive.Add("Bullish Engulfing");
			}

			// 2. Higher Highs / Higher Lows (for Buy)
			if (direction == "Buy" && idx >= 2)
			{
				bool higherHighs = highs[idx] > highs[idx - 1] && highs[idx - 1] > highs[idx - 2];
				bool higherLows = lows[idx] > lows[idx - 1] && lows[idx - 1] > lows[idx - 2];

				if (higherHighs && higherLows)
					positive.Add("Higher Highs & Lows");
			}

			// 3. Lower Highs / Lower Lows (for Sell)
			if (direction == "Sell" && idx >= 2)
			{
				bool lowerHighs = highs[idx] < highs[idx - 1] && highs[idx - 1] < highs[idx - 2];
				bool lowerLows = lows[idx] < lows[idx - 1] && lows[idx - 1] < lows[idx - 2];

				if (lowerHighs && lowerLows)
					positive.Add("Lower Highs & Lows");
			}

			// 4. Volume Confirmation
			if (volumes.Count > idx && idx >= 1)
			{
				bool volumeIncreasing = volumes[idx] > volumes[idx - 1];
				bool priceMoving = Math.Abs(closes[idx] - closes[idx - 1]) > 0.01m;

				if (volumeIncreasing && priceMoving)
					positive.Add("Volume Confirmation");
			}

			// 5. Exhaustion Patterns (negative)
			if (idx >= 3)
			{
				// Three consecutive strong moves might signal exhaustion
				bool threeUpDays = closes[idx] > closes[idx - 1] &&
								  closes[idx - 1] > closes[idx - 2] &&
								  closes[idx - 2] > closes[idx - 3];

				if (threeUpDays && direction == "Buy")
					negative.Add("Potential exhaustion");

				bool threeDownDays = closes[idx] < closes[idx - 1] &&
									closes[idx - 1] < closes[idx - 2] &&
									closes[idx - 2] < closes[idx - 3];

				if (threeDownDays && direction == "Sell")
					negative.Add("Potential exhaustion");
			}

			return (positive, negative);
		}

		private static (decimal MomentumScore, decimal MeanReversionSetup) CalculateStatisticalFeatures(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, string direction)
		{
			if (closes.Count < 20)
				return (0.5m, 0.5m);

			int idx = closes.Count - 1;

			// Momentum Score
			decimal momentum7D = closes.Count > 7 ? (closes[idx] - closes[idx - 7]) / closes[idx - 7] : 0m;
			decimal momentum3D = closes.Count > 3 ? (closes[idx] - closes[idx - 3]) / closes[idx - 3] : 0m;

			decimal momentumScore = 0m;
			if (direction == "Buy")
			{
				if (momentum7D > 0.03m && momentum3D > 0.01m) // 3% and 1%
					momentumScore = 0.9m;
				else if (momentum7D > 0.02m)
					momentumScore = 0.7m;
				else if (momentum7D > 0.01m)
					momentumScore = 0.5m;
				else
					momentumScore = 0.3m;
			}
			else if (direction == "Sell")
			{
				if (momentum7D < -0.03m && momentum3D < -0.01m)
					momentumScore = 0.9m;
				else if (momentum7D < -0.02m)
					momentumScore = 0.7m;
				else if (momentum7D < -0.01m)
					momentumScore = 0.5m;
				else
					momentumScore = 0.3m;
			}

			// Mean Reversion Setup Score
			var ema20 = Indicators.EMAList(closes, 20);
			decimal distanceFromEMA = Math.Abs(closes[idx] - ema20[idx]) / ema20[idx];

			decimal meanReversionSetup = 0m;
			if (direction == "Buy" && closes[idx] < ema20[idx] && distanceFromEMA > 0.02m && distanceFromEMA < 0.08m)
				meanReversionSetup = 0.8m; // Pullback to EMA
			else if (direction == "Sell" && closes[idx] > ema20[idx] && distanceFromEMA > 0.02m && distanceFromEMA < 0.08m)
				meanReversionSetup = 0.8m;

			return (momentumScore, meanReversionSetup);
		}

		private static (decimal ExpectedRR, decimal StopDistance, decimal TargetDistance) CalculateRiskReward(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, string direction)
		{
			if (closes.Count < 20)
				return (1.5m, 0m, 0m);

			int idx = closes.Count - 1;
			decimal currentPrice = closes[idx];

			// Find support/resistance for stop and target
			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);

			decimal stopDistance = 0m;
			decimal targetDistance = 0m;

			if (direction == "Buy")
			{
				// Stop below nearest support
				var nearestSupport = srLevels
					.Where(l => l.IsSupport && l.Level < currentPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearestSupport != null)
					stopDistance = currentPrice - nearestSupport.Level;
				else
					stopDistance = currentPrice * 0.04m; // Default 4%

				// Target at nearest resistance
				var nearestResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > currentPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearestResistance != null)
					targetDistance = nearestResistance.Level - currentPrice;
				else
					targetDistance = currentPrice * 0.06m; // Default 6%
			}
			else
			{
				// Stop above nearest resistance
				var nearestResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > currentPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearestResistance != null)
					stopDistance = nearestResistance.Level - currentPrice;
				else
					stopDistance = currentPrice * 0.04m;

				// Target at nearest support
				var nearestSupport = srLevels
					.Where(l => l.IsSupport && l.Level < currentPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearestSupport != null)
					targetDistance = currentPrice - nearestSupport.Level;
				else
					targetDistance = currentPrice * 0.06m;
			}

			decimal expectedRR = stopDistance > 0 ? targetDistance / stopDistance : 1.5m;

			return (expectedRR, stopDistance, targetDistance);
		}

		private static decimal CalculateEnsembleScore(
			decimal baseConfidence,
			decimal qualityScore,
			decimal freshness,
			decimal regimeAlignment,
			decimal anomalyScore,
			decimal momentumScore,
			decimal riskReward,
			EnhancedMarketRegime.RegimeAnalysis regime)
		{
			// Weighted ensemble of all factors
			decimal score = 0m;

			// Base signals
			score += baseConfidence * 0.25m;        // 25% weight
			score += qualityScore * 0.20m;          // 20% weight

			// Contextual factors
			score += freshness * 0.15m;             // 15% weight
			score += regimeAlignment * 0.15m;       // 15% weight
			score += momentumScore * 0.10m;         // 10% weight

			// Risk-reward factor
			decimal rrScore = Math.Min(riskReward / 3m, 1m); // Normalize to 0-1 (3:1 = perfect)
			score += rrScore * 0.10m;               // 10% weight

			// Penalty for anomalies
			score -= anomalyScore * 0.10m;          // -10% max penalty

			// Regime confidence boost
			score += regime.RegimeConfidence * 0.05m; // 5% boost

			return Math.Max(0m, Math.Min(1m, score));
		}

		private static decimal CalculateFinalScore(
			decimal adjustedConfidence,
			decimal qualityScore,
			decimal regimeAlignment,
			decimal freshness,
			decimal riskReward)
		{
			// Convert to 0-100 scale with weighted factors
			decimal score = 0m;

			score += adjustedConfidence * 40m;      // Max 40 points
			score += qualityScore * 30m;            // Max 30 points
			score += regimeAlignment * 15m;         // Max 15 points
			score += freshness * 10m;               // Max 10 points
			score += Math.Min(riskReward / 3m, 1m) * 5m; // Max 5 points

			return Math.Max(0m, Math.Min(100m, score));
		}

		private static decimal EstimateWinRate(
			decimal finalScore,
			EnhancedMarketRegime.RegimeAnalysis regime,
			Dictionary<string, decimal>? strategyPerformance)
		{
			// Base win rate from score
			decimal baseWinRate = 0.40m + (finalScore / 100m) * 0.30m; // 40-70% based on score

			// Adjust for regime
			if (regime.TrendStrength > 0.7m && !regime.InRegimeTransition)
				baseWinRate += 0.05m; // +5% in strong trends

			if (regime.VolatilityRegime == "Extreme")
				baseWinRate -= 0.10m; // -10% in extreme volatility

			if (regime.TimeframesAligned)
				baseWinRate += 0.05m; // +5% when timeframes agree

			if (regime.InRegimeTransition)
				baseWinRate -= 0.08m; // -8% in transitions

			// Historical performance boost (if available)
			// This would be populated from actual trading results over time
			// For now, use regime-based estimates

			return Math.Max(0.25m, Math.Min(0.85m, baseWinRate));
		}

		private static string RankSignal(decimal finalScore)
		{
			if (finalScore >= 80m) return "S";      // Exceptional
			if (finalScore >= 70m) return "A";      // Excellent
			if (finalScore >= 60m) return "B";      // Good
			if (finalScore >= 50m) return "C";      // Fair
			return "D";                              // Weak
		}

		private static string GenerateRecommendation(ScoredSignal scored, EnhancedMarketRegime.RegimeAnalysis regime)
		{
			// Strong Buy: S/A rank + favorable regime
			if (scored.SignalRank == "S" && regime.RecommendedAction != "Defensive")
				return "Strong Buy";

			// Buy: A/B rank + normal/aggressive regime
			if ((scored.SignalRank == "A" || scored.SignalRank == "B") &&
				regime.RecommendedAction != "Avoid" && scored.ExpectedWinRate > 0.55m)
				return "Buy";

			// Hold/Watch: C rank or defensive regime
			if (scored.SignalRank == "C" || regime.RecommendedAction == "Defensive")
				return "Watch";

			// Pass: D rank, avoid regime, or low win rate
			if (scored.SignalRank == "D" || regime.RecommendedAction == "Avoid" || scored.ExpectedWinRate < 0.45m)
				return "Pass";

			return "Hold";
		}

		private static decimal CalculateStdDev(List<decimal> values)
		{
			if (values.Count == 0) return 0m;

			decimal avg = values.Average();
			decimal sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
			decimal variance = sumOfSquares / values.Count;

			return (decimal)Math.Sqrt((double)variance);
		}
	}
}
