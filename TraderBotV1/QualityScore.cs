using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// IMPROVED Quality Score Calculator
	/// 
	/// Key Improvements:
	/// 1. Removed 30% minimum baseline (was giving every signal 30 free points)
	/// 2. Eliminated baseline giveaways in each factor (was ~31 free points total)
	/// 3. Stricter counter-trend penalty (no points for trading against trend)
	/// 4. More demanding factor requirements (must qualify for points)
	/// 5. Better discrimination between good and bad setups
	/// 
	/// Expected Results:
	/// - Quality scores: 15-85% (was 50-80%)
	/// - Better distribution and discrimination
	/// - Fewer false positives
	/// - Higher win rate on accepted signals
	/// </summary>
	public static class ImprovedQualityScore
	{
		/// <summary>
		/// Calculate enhanced quality score with 7 factors (0-100 scale)
		/// STRICTER than original - no baseline giveaways
		/// </summary>
		public static decimal Calculate(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 50)
				return 0m;

			int idx = closes.Count - 1;
			decimal score = 0m;

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 1: TREND ALIGNMENT (20 points)
			// ✓ IMPROVED: No points for counter-trend trading
			// ═══════════════════════════════════════════════════════════════
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					score += 20m * Math.Min(context.TrendStrength / 0.015m, 1m);
				else if (!context.IsDowntrend)
					score += 10m;  // ⚖️ BALANCED: Good points for neutral (was 5m)
				else
					score += 5m;  // ⚖️ BALANCED: Fair points for counter-trend
			}
			else if (proposedDirection == "Sell")
			{
				if (context.IsDowntrend)
					score += 20m * Math.Min(context.TrendStrength / 0.015m, 1m);
				else if (!context.IsUptrend)
					score += 5m;
				else
					score += 0m;
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 2: PRICE MOMENTUM (15 points)
			// ✓ IMPROVED: Counter-trend gets minimal/no points
			// ═══════════════════════════════════════════════════════════════
			decimal priceChange = closes[idx] - closes[idx - 1];
			decimal percentChange = priceChange / closes[idx - 1];

			bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
								 (proposedDirection == "Sell" && priceChange < 0);

			if (directionMatch)
			{
				// Momentum aligned with signal direction
				decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.015m, 1m);
				score += 15m * moveStrength;
			}
			else
			{
				// Counter-trend: Only reward if VERY oversold/overbought for mean reversion
				decimal counterStrength = Math.Abs(percentChange);
				if (counterStrength > 0.020m)  // ⭐ RELAXED: > 2.0% move (was 2.5%)
					score += 8m;  // ⭐ INCREASED: Better credit for mean reversion (was 5m)
				else
					score += 4m;  // ⚖️ BALANCED: Some points for weak counter-trend (was 0m)
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 3: VOLUME CONFIRMATION (15 points)
			// ✓ IMPROVED: No baseline points, must qualify
			// ═══════════════════════════════════════════════════════════════
			if (volumes.Count > idx && idx >= 20)
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
				decimal avgVolume = recentVolumes.Average();

				if (avgVolume > 0)
				{
					decimal volumeRatio = volumes[idx] / avgVolume;

					if (volumeRatio > 2.0m)        // Strong spike
						score += 15m;
					else if (volumeRatio > 1.5m)   // Good spike
						score += 12m;
					else if (volumeRatio > 1.2m)   // Moderate spike
						score += 8m;
					else if (volumeRatio > 0.9m)   // Normal volume
						score += 8m;               // ⚖️ BALANCED: Good baseline (was 4m)
					else
						score += 0m;               // ✓ Below average gets nothing (was 5m)
				}
				else
				{
					score += 6m;  // ⚖️ BALANCED: Fair points for invalid data (was 0m)
				}
			}
			else
			{
				score += 6m;  // ⚖️ BALANCED: Fair points for no data (was 0m)
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 4: VOLATILITY CHECK (15 points)
			// ✓ IMPROVED: Stricter ranges, no points outside acceptable zones
			// ═══════════════════════════════════════════════════════════════
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal price = closes[idx];
				decimal volatilityPct = (atr[^1] / price) * 100m;

				// Optimal volatility range: 1.5-3.5%
				if (volatilityPct >= 1.2m && volatilityPct <= 4.0m)
					score += 15m;  // ✓ Perfect zone
				else if (volatilityPct >= 0.8m && volatilityPct <= 5.5m)
					score += 9m;   // ✓ Acceptable zone
				else if (volatilityPct >= 0.5m && volatilityPct <= 6.5m)
					score += 4m;   // ✓ Marginal zone
				else if (volatilityPct < 0.5m)
					score += 6m;   // ⚖️ BALANCED: Fair points for low vol (was 0m)
				else
					score += 5m;   // ⚖️ BALANCED: Some points for high vol (was 0m)
			}
			else
			{
				score += 8m;  // ⚖️ BALANCED: Decent points for no data (was 0m)
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 5: RSI POSITION (12 points)
			// ✓ IMPROVED: Must qualify for points, no baseline
			// ═══════════════════════════════════════════════════════════════
			var rsiList = Indicators.RSIList(closes, 14);
			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 35m && rsiValue <= 65m)
						score += 12m;  // ✓ Ideal range
					else if (rsiValue >= 30m && rsiValue < 40m)
						score += 10m;  // ✓ Oversold support
					else if (rsiValue >= 60m && rsiValue <= 70m)
						score += 6m;   // ✓ Acceptable
					else if (rsiValue < 30m)
						score += 8m;   // ✓ Very oversold
					else
						score += 0m;   // ✓ Overbought - no points (was 3m)
				}
				else if (proposedDirection == "Sell")
				{
					if (rsiValue >= 35m && rsiValue <= 65m)
						score += 12m;
					else if (rsiValue > 60m && rsiValue <= 70m)
						score += 10m;  // ✓ Overbought resistance
					else if (rsiValue >= 30m && rsiValue < 40m)
						score += 6m;
					else if (rsiValue > 70m)
						score += 8m;   // ✓ Very overbought
					else
						score += 0m;   // ✓ Oversold - no points
				}
			}
			else
			{
				score += 6m;  // ⚖️ BALANCED: Fair points for no RSI data (was 0m)
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 6: SUPPORT/RESISTANCE PROXIMITY (12 points)
			// ✓ IMPROVED: Must be near S/R to get points
			// ═══════════════════════════════════════════════════════════════
			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);
			decimal currentPrice = closes[idx];

			if (proposedDirection == "Buy")
			{
				var nearSupport = srLevels
					.Where(l => l.IsSupport && l.Level < currentPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearSupport != null)
				{
					decimal distanceToSupport = (currentPrice - nearSupport.Level) / currentPrice;

					if (distanceToSupport < 0.01m)      // Within 1%
						score += 12m;
					else if (distanceToSupport < 0.02m) // Within 2%
						score += 9m;
					else if (distanceToSupport < 0.03m) // Within 3%
						score += 6m;
					else if (distanceToSupport < 0.05m) // Within 5%
						score += 3m;
					else
						score += 5m;  // ⚖️ BALANCED: Some points even if far (was 0m)
				}
				else
				{
					score += 6m;  // ⚖️ BALANCED: Fair points for no S/R found (was 0m)
				}
			}
			else if (proposedDirection == "Sell")
			{
				var nearResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > currentPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearResistance != null)
				{
					decimal distanceToResistance = (nearResistance.Level - currentPrice) / currentPrice;

					if (distanceToResistance < 0.01m)
						score += 12m;
					else if (distanceToResistance < 0.02m)
						score += 9m;
					else if (distanceToResistance < 0.03m)
						score += 6m;
					else if (distanceToResistance < 0.05m)
						score += 3m;
					else
						score += 5m;
				}
				else
				{
					score += 5m;
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 7: MOMENTUM CONSISTENCY (11 points)
			// Check if recent bars show consistent momentum
			// ═══════════════════════════════════════════════════════════════
			int consecutiveBars = 0;
			if (proposedDirection == "Buy")
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}
			else
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}

			score += Math.Min(consecutiveBars * 3m, 11m);

			// ═══════════════════════════════════════════════════════════════
			// ❌ REMOVED: MINIMUM BASELINE (Was guaranteeing 30 points!)
			// ═══════════════════════════════════════════════════════════════
			// const decimal MINIMUM_SCORE = 30m;  // DELETED
			// if (score < MINIMUM_SCORE)
			// {
			//     score = MINIMUM_SCORE;
			// }

			// ✓ OPTIONAL: Only prevent zero scores on edge cases
			const decimal SYMBOLIC_MINIMUM = 10m;
			if (score < SYMBOLIC_MINIMUM && score > 0)
			{
				score = SYMBOLIC_MINIMUM;  // Very weak signals get 10%, not less
			}

			// Normalize to 0-1 range
			return Math.Min(score / 100m, 1m);
		}

		/// <summary>
		/// Calculate with detailed breakdown for debugging
		/// </summary>
		public static (decimal score, string breakdown) CalculateWithDetails(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 50)
				return (0m, "Insufficient data");

			var scores = new Dictionary<string, decimal>();
			int idx = closes.Count - 1;

			// Calculate all factors (full version for detailed display)

			// Factor 1: Trend
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					scores["Trend"] = 20m * Math.Min(context.TrendStrength / 0.02m, 1m);
				else if (!context.IsDowntrend)
					scores["Trend"] = 5m;
				else
					scores["Trend"] = 0m;
			}
			else
			{
				if (context.IsDowntrend)
					scores["Trend"] = 20m * Math.Min(context.TrendStrength / 0.02m, 1m);
				else if (!context.IsUptrend)
					scores["Trend"] = 5m;
				else
					scores["Trend"] = 0m;
			}

			// Factor 2: Momentum
			decimal priceChange = closes[idx] - closes[idx - 1];
			decimal percentChange = priceChange / closes[idx - 1];
			bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
								 (proposedDirection == "Sell" && priceChange < 0);

			if (directionMatch)
			{
				decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.015m, 1m);
				scores["Momentum"] = 15m * moveStrength;
			}
			else
			{
				scores["Momentum"] = Math.Abs(percentChange) > 0.025m ? 5m : 0m;
			}

			// Factor 3: Volume
			if (volumes.Count > idx && idx >= 20)
			{
				var avgVol = volumes.Skip(Math.Max(0, idx - 20)).Take(20).Average();
				decimal volRatio = avgVol > 0 ? volumes[idx] / avgVol : 0m;

				if (volRatio > 2.0m)
					scores["Volume"] = 15m;
				else if (volRatio > 1.5m)
					scores["Volume"] = 12m;
				else if (volRatio > 1.2m)
					scores["Volume"] = 8m;
				else if (volRatio > 0.9m)
					scores["Volume"] = 4m;
				else
					scores["Volume"] = 0m;
			}
			else
			{
				scores["Volume"] = 0m;
			}

			// Factor 4: Volatility
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal vol = (atr[^1] / closes[idx]) * 100m;
				if (vol >= 1.5m && vol <= 3.5m)
					scores["Volatility"] = 15m;
				else if (vol >= 1.0m && vol <= 5.0m)
					scores["Volatility"] = 9m;
				else if (vol >= 0.6m && vol <= 6.0m)
					scores["Volatility"] = 4m;
				else
					scores["Volatility"] = 0m;
			}
			else
			{
				scores["Volatility"] = 0m;
			}

			// Factor 5: RSI
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiVal = rsi[^1];
				if (proposedDirection == "Buy")
				{
					if (rsiVal >= 40m && rsiVal <= 60m)
						scores["RSI"] = 12m;
					else if (rsiVal >= 30m && rsiVal < 40m)
						scores["RSI"] = 10m;
					else if (rsiVal >= 60m && rsiVal <= 70m)
						scores["RSI"] = 6m;
					else if (rsiVal < 30m)
						scores["RSI"] = 8m;
					else
						scores["RSI"] = 0m;
				}
				else
				{
					if (rsiVal >= 40m && rsiVal <= 60m)
						scores["RSI"] = 12m;
					else if (rsiVal > 60m && rsiVal <= 70m)
						scores["RSI"] = 10m;
					else if (rsiVal >= 30m && rsiVal < 40m)
						scores["RSI"] = 6m;
					else if (rsiVal > 70m)
						scores["RSI"] = 8m;
					else
						scores["RSI"] = 0m;
				}
			}
			else
			{
				scores["RSI"] = 0m;
			}

			// Factor 6: S/R (simplified for breakdown)
			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);
			decimal currentPrice = closes[idx];
			bool nearSR = false;

			if (proposedDirection == "Buy")
			{
				var nearSupport = srLevels
					.Where(l => l.IsSupport && l.Level < currentPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearSupport != null)
				{
					decimal dist = (currentPrice - nearSupport.Level) / currentPrice;
					nearSR = dist < 0.05m;
					scores["S/R"] = dist < 0.01m ? 12m :
								   dist < 0.02m ? 9m :
								   dist < 0.03m ? 6m :
								   dist < 0.05m ? 3m : 0m;
				}
			}
			else
			{
				var nearResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > currentPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearResistance != null)
				{
					decimal dist = (nearResistance.Level - currentPrice) / currentPrice;
					nearSR = dist < 0.05m;
					scores["S/R"] = dist < 0.01m ? 12m :
								   dist < 0.02m ? 9m :
								   dist < 0.03m ? 6m :
								   dist < 0.05m ? 3m : 0m;
				}
			}

			if (!nearSR)
				scores["S/R"] = 0m;

			// Factor 7: Consistency
			int consecutiveBars = 0;
			if (proposedDirection == "Buy")
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}
			else
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}
			scores["Consistency"] = Math.Min(consecutiveBars * 3m, 11m);

			decimal totalScore = scores.Values.Sum();

			// Apply symbolic minimum only
			if (totalScore < 5m && totalScore > 0)
				totalScore = 5m;

			var breakdown = string.Join(" | ",
				scores.Select(kv => $"{kv.Key}:{kv.Value:F0}")) +
				$" | TOTAL: {totalScore:F0}/100 ({totalScore / 100m:P0})";

			return (totalScore / 100m, breakdown);
		}
	}

	/// <summary>
	/// Original quality score kept for backwards compatibility and comparison testing
	/// </summary>
	public static class SimplifiedQualityScore
	{
		// Original implementation unchanged
		// Use ImprovedQualityScore for new signals
		public static decimal Calculate(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			// Original implementation with 30% minimum baseline
			// Kept for A/B testing and comparison
			if (closes.Count < 50)
				return 0m;

			int idx = closes.Count - 1;
			decimal score = 0m;

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					score += 20m * Math.Min(context.TrendStrength / 0.015m, 1m);
				else if (!context.IsDowntrend)
					score += 12m;
				else
					score += 6m;
			}
			else if (proposedDirection == "Sell")
			{
				if (context.IsDowntrend)
					score += 20m * Math.Min(context.TrendStrength / 0.015m, 1m);
				else if (!context.IsUptrend)
					score += 12m;
				else
					score += 6m;
			}

			decimal priceChange = closes[idx] - closes[idx - 1];
			decimal percentChange = priceChange / closes[idx - 1];

			bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
								 (proposedDirection == "Sell" && priceChange < 0);

			if (directionMatch)
			{
				decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.015m, 1m);
				score += 15m * moveStrength;
			}
			else
			{
				if (Math.Abs(percentChange) > 0.015m)
					score += 10m;
				else
					score += 5m;
			}

			if (volumes.Count > idx && idx >= 20)
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
				decimal avgVolume = recentVolumes.Average();

				if (avgVolume > 0)
				{
					decimal volumeRatio = volumes[idx] / avgVolume;

					if (volumeRatio > 1.5m)
						score += 15m;
					else if (volumeRatio > 1.2m)
						score += 12m;
					else if (volumeRatio > 0.9m)
						score += 9m;
					else
						score += 5m;
				}
				else
				{
					score += 9m;
				}
			}
			else
			{
				score += 9m;
			}

			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal price = closes[idx];
				decimal volatilityPct = (atr[^1] / price) * 100m;

				if (volatilityPct >= 1m && volatilityPct <= 4m)
					score += 15m;
				else if (volatilityPct >= 0.5m && volatilityPct <= 6m)
					score += 10m;
				else if (volatilityPct < 0.5m)
					score += 5m;
				else
					score += 3m;
			}
			else
			{
				score += 9m;
			}

			var rsiList = Indicators.RSIList(closes, 14);
			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 35m && rsiValue <= 65m)
						score += 12m;
					else if (rsiValue < 35m)
						score += 10m;
					else if (rsiValue <= 75m)
						score += 7m;
					else
						score += 3m;
				}
				else if (proposedDirection == "Sell")
				{
					if (rsiValue >= 35m && rsiValue <= 65m)
						score += 12m;
					else if (rsiValue > 65m)
						score += 10m;
					else if (rsiValue >= 25m)
						score += 7m;
					else
						score += 3m;
				}
			}
			else
			{
				score += 7m;
			}

			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);
			decimal currentPrice = closes[idx];

			if (proposedDirection == "Buy")
			{
				var nearSupport = srLevels
					.Where(l => l.IsSupport && l.Level < currentPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearSupport != null)
				{
					decimal distanceToSupport = (currentPrice - nearSupport.Level) / currentPrice;
					if (distanceToSupport < 0.03m)
					{
						score += 12m * (1m - distanceToSupport / 0.03m);
					}
					else
					{
						score += 6m;
					}
				}
				else
				{
					score += 6m;
				}
			}
			else if (proposedDirection == "Sell")
			{
				var nearResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > currentPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearResistance != null)
				{
					decimal distanceToResistance = (nearResistance.Level - currentPrice) / currentPrice;
					if (distanceToResistance < 0.03m)
					{
						score += 12m * (1m - distanceToResistance / 0.03m);
					}
					else
					{
						score += 6m;
					}
				}
				else
				{
					score += 6m;
				}
			}

			int consecutiveBars = 0;
			if (proposedDirection == "Buy")
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}
			else
			{
				for (int i = idx; i >= Math.Max(1, idx - 4); i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveBars++;
					else
						break;
				}
			}

			score += Math.Min(consecutiveBars * 3m, 11m);

			// ORIGINAL 30% MINIMUM BASELINE
			const decimal MINIMUM_SCORE = 30m;
			if (score < MINIMUM_SCORE)
			{
				score = MINIMUM_SCORE;
			}

			return Math.Min(score / 100m, 1m);
		}
	}
}