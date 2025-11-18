using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// SWING TRADING QUALITY SCORE CALCULATOR
	/// Optimized for 1-2 week holding periods on DAILY bars
	/// 
	/// Key Adjustments:
	/// 1. Longer trend analysis periods (20-day vs 5-day)
	/// 2. Wider volatility acceptable ranges
	/// 3. More lenient volume requirements  
	/// 4. Larger S/R proximity tolerances
	/// 5. Momentum consistency over 3-5 days (not 2-3)
	/// </summary>
	public static class SwingTradingQualityScore
	{
		/// <summary>
		/// Calculate swing trading quality score (0-100 scale)
		/// Optimized for daily bars and 1-2 week holds
		/// </summary>
		public static decimal Calculate(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 60)  // ⭐ SWING: Need 60+ days
				return 0m;

			int idx = closes.Count - 1;
			decimal score = 0m;

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 1: TREND ALIGNMENT (22 points)
			// ⭐ SWING: Higher weight on trend-following
			// ═══════════════════════════════════════════════════════════════
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					score += 22m * Math.Min(context.TrendStrength / 0.012m, 1m);  // ⭐ 1.2% trend strength target
				else if (!context.IsDowntrend)
					score += 12m;  // Neutral trend okay
				else
					score += 3m;   // Counter-trend needs exceptional setup
			}
			else if (proposedDirection == "Sell")
			{
				if (context.IsDowntrend)
					score += 22m * Math.Min(context.TrendStrength / 0.012m, 1m);
				else if (!context.IsUptrend)
					score += 12m;
				else
					score += 3m;
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 2: PRICE MOMENTUM (18 points)
			// ⭐ SWING: Evaluate 3-day momentum (not just 1-day)
			// ═══════════════════════════════════════════════════════════════
			if (idx >= 3)
			{
				decimal price3DaysAgo = closes[idx - 3];
				decimal priceChange = closes[idx] - price3DaysAgo;
				decimal percentChange = priceChange / price3DaysAgo;

				bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
									 (proposedDirection == "Sell" && priceChange < 0);

				if (directionMatch)
				{
					// Momentum aligned - good for swing trades
					decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.03m, 1m);  // ⭐ 3% target move
					score += 18m * moveStrength;
				}
				else
				{
					// Counter-trend mean reversion - needs strong justification
					decimal counterStrength = Math.Abs(percentChange);
					if (counterStrength > 0.05m)  // ⭐ SWING: >5% move for mean reversion
						score += 12m;
					else if (counterStrength > 0.03m)
						score += 6m;
					else
						score += 3m;  // Weak counter-trend setup
				}
			}
			else
			{
				score += 9m;  // Not enough history
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 3: VOLUME CONFIRMATION (14 points)
			// ⭐ SWING: More lenient volume requirements
			// ═══════════════════════════════════════════════════════════════
			if (volumes.Count > idx && idx >= 30)  // ⭐ SWING: 30-day average
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 30)).Take(30).ToList();
				decimal avgVolume = recentVolumes.Average();

				if (avgVolume > 0)
				{
					decimal volumeRatio = volumes[idx] / avgVolume;

					if (volumeRatio > 1.8m)        // Strong spike
						score += 14m;
					else if (volumeRatio > 1.4m)   // Good spike
						score += 12m;
					else if (volumeRatio > 1.1m)   // Moderate spike
						score += 10m;
					else if (volumeRatio > 0.7m)   // ⭐ SWING: Normal/below average okay
						score += 8m;
					else
						score += 4m;               // Very low volume warning
				}
				else
				{
					score += 7m;  // Invalid data
				}
			}
			else
			{
				score += 7m;  // No volume data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 4: VOLATILITY CHECK (14 points)
			// ⭐ SWING: Wider acceptable volatility ranges
			// ═══════════════════════════════════════════════════════════════
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal price = closes[idx];
				decimal volatilityPct = (atr[^1] / price) * 100m;

				// ⭐ SWING: Optimal volatility range: 1.5-4.5% (wider than day trading)
				if (volatilityPct >= 1.5m && volatilityPct <= 4.5m)
					score += 14m;  // Perfect zone for swing trades
				else if (volatilityPct >= 1.0m && volatilityPct <= 6.0m)
					score += 11m;  // Acceptable zone
				else if (volatilityPct >= 0.7m && volatilityPct <= 7.0m)
					score += 7m;   // Marginal zone
				else if (volatilityPct < 0.7m)
					score += 4m;   // Too quiet for swing trading
				else
					score += 5m;   // Too volatile
			}
			else
			{
				score += 7m;  // No ATR data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 5: RSI POSITION (13 points)
			// ⭐ SWING: Swing-appropriate RSI levels
			// ═══════════════════════════════════════════════════════════════
			var rsiList = Indicators.RSIList(closes, 14);
			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 35m && rsiValue <= 60m)
						score += 13m;  // ⭐ SWING: Ideal buy range
					else if (rsiValue >= 25m && rsiValue < 35m)
						score += 11m;  // Oversold support
					else if (rsiValue >= 60m && rsiValue <= 68m)
						score += 8m;   // Acceptable but extended
					else if (rsiValue < 25m)
						score += 9m;   // Very oversold - potential bounce
					else
						score += 3m;   // Too overbought
				}
				else if (proposedDirection == "Sell")
				{
					if (rsiValue >= 40m && rsiValue <= 65m)
						score += 13m;  // ⭐ SWING: Ideal sell range
					else if (rsiValue > 65m && rsiValue <= 75m)
						score += 11m;  // Overbought resistance
					else if (rsiValue >= 32m && rsiValue < 40m)
						score += 8m;   // Acceptable but extended
					else if (rsiValue > 75m)
						score += 9m;   // Very overbought - potential drop
					else
						score += 3m;   // Too oversold
				}
			}
			else
			{
				score += 7m;  // No RSI data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 6: SUPPORT/RESISTANCE PROXIMITY (10 points)
			// ⭐ SWING: Wider tolerance for S/R proximity
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

					// ⭐ SWING: More lenient distance requirements
					if (distanceToSupport < 0.015m)      // Within 1.5%
						score += 10m;
					else if (distanceToSupport < 0.03m)  // Within 3%
						score += 8m;
					else if (distanceToSupport < 0.05m)  // Within 5%
						score += 6m;
					else if (distanceToSupport < 0.08m)  // Within 8%
						score += 4m;
					else
						score += 6m;  // Far from support - still okay
				}
				else
				{
					score += 6m;  // No S/R found - neutral
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

					if (distanceToResistance < 0.015m)
						score += 10m;
					else if (distanceToResistance < 0.03m)
						score += 8m;
					else if (distanceToResistance < 0.05m)
						score += 6m;
					else if (distanceToResistance < 0.08m)
						score += 4m;
					else
						score += 6m;
				}
				else
				{
					score += 6m;
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 7: MOMENTUM CONSISTENCY (9 points)
			// ⭐ SWING: Check 5-day consistency (not 3-day)
			// ═══════════════════════════════════════════════════════════════
			int consecutiveDays = 0;
			if (proposedDirection == "Buy")
			{
				for (int i = idx; i >= Math.Max(1, idx - 5); i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveDays++;
					else
						break;
				}
			}
			else
			{
				for (int i = idx; i >= Math.Max(1, idx - 5); i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveDays++;
					else
						break;
				}
			}

			// ⭐ SWING: Up to 9 points for consistency
			score += Math.Min(consecutiveDays * 2m, 9m);

			// ═══════════════════════════════════════════════════════════════
			// FINAL: Normalize to 0-1 scale
			// Total possible: 100 points
			// ═══════════════════════════════════════════════════════════════
			return Math.Min(score / 100m, 1m);
		}

		/// <summary>
		/// Get detailed quality breakdown for debugging
		/// </summary>
		public static (decimal score, string breakdown) CalculateWithBreakdown(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 60)
				return (0m, "Insufficient data (need 60+ bars)");

			int idx = closes.Count - 1;
			var scores = new Dictionary<string, decimal>();

			// Factor 1: Trend (22 pts)
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					scores["Trend"] = 22m * Math.Min(context.TrendStrength / 0.012m, 1m);
				else if (!context.IsDowntrend)
					scores["Trend"] = 12m;
				else
					scores["Trend"] = 3m;
			}
			else
			{
				if (context.IsDowntrend)
					scores["Trend"] = 22m * Math.Min(context.TrendStrength / 0.012m, 1m);
				else if (!context.IsUptrend)
					scores["Trend"] = 12m;
				else
					scores["Trend"] = 3m;
			}

			// Factor 2: Momentum (18 pts)
			if (idx >= 3)
			{
				decimal priceChange = closes[idx] - closes[idx - 3];
				decimal percentChange = priceChange / closes[idx - 3];
				bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
									 (proposedDirection == "Sell" && priceChange < 0);

				if (directionMatch)
				{
					decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.03m, 1m);
					scores["Momentum"] = 18m * moveStrength;
				}
				else
				{
					decimal counterStrength = Math.Abs(percentChange);
					scores["Momentum"] = counterStrength > 0.05m ? 12m :
										counterStrength > 0.03m ? 6m : 3m;
				}
			}
			else
			{
				scores["Momentum"] = 9m;
			}

			// Factor 3: Volume (14 pts)
			if (volumes.Count > idx && idx >= 30)
			{
				var recentVols = volumes.Skip(Math.Max(0, idx - 30)).Take(30).ToList();
				decimal avgVol = recentVols.Average();
				if (avgVol > 0)
				{
					decimal ratio = volumes[idx] / avgVol;
					scores["Volume"] = ratio > 1.8m ? 14m :
									  ratio > 1.4m ? 12m :
									  ratio > 1.1m ? 10m :
									  ratio > 0.7m ? 8m : 4m;
				}
				else
				{
					scores["Volume"] = 7m;
				}
			}
			else
			{
				scores["Volume"] = 7m;
			}

			// Factor 4: Volatility (14 pts)
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal volPct = (atr[^1] / closes[idx]) * 100m;
				scores["Volatility"] = volPct >= 1.5m && volPct <= 4.5m ? 14m :
									  volPct >= 1.0m && volPct <= 6.0m ? 11m :
									  volPct >= 0.7m && volPct <= 7.0m ? 7m :
									  volPct < 0.7m ? 4m : 5m;
			}
			else
			{
				scores["Volatility"] = 7m;
			}

			// Factor 5: RSI (13 pts)
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiVal = rsi[^1];
				if (proposedDirection == "Buy")
				{
					scores["RSI"] = rsiVal >= 35m && rsiVal <= 60m ? 13m :
								   rsiVal >= 25m && rsiVal < 35m ? 11m :
								   rsiVal >= 60m && rsiVal <= 68m ? 8m :
								   rsiVal < 25m ? 9m : 3m;
				}
				else
				{
					scores["RSI"] = rsiVal >= 40m && rsiVal <= 65m ? 13m :
								   rsiVal > 65m && rsiVal <= 75m ? 11m :
								   rsiVal >= 32m && rsiVal < 40m ? 8m :
								   rsiVal > 75m ? 9m : 3m;
				}
			}
			else
			{
				scores["RSI"] = 7m;
			}

			// Factor 6: S/R (10 pts)
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
					decimal dist = (currentPrice - nearSupport.Level) / currentPrice;
					scores["S/R"] = dist < 0.015m ? 10m :
								   dist < 0.03m ? 8m :
								   dist < 0.05m ? 6m :
								   dist < 0.08m ? 4m : 6m;
				}
				else
				{
					scores["S/R"] = 6m;
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
					scores["S/R"] = dist < 0.015m ? 10m :
								   dist < 0.03m ? 8m :
								   dist < 0.05m ? 6m :
								   dist < 0.08m ? 4m : 6m;
				}
				else
				{
					scores["S/R"] = 6m;
				}
			}

			// Factor 7: Consistency (9 pts)
			int consecutiveDays = 0;
			if (proposedDirection == "Buy")
			{
				for (int i = idx; i >= Math.Max(1, idx - 5); i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveDays++;
					else
						break;
				}
			}
			else
			{
				for (int i = idx; i >= Math.Max(1, idx - 5); i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveDays++;
					else
						break;
				}
			}
			scores["Consistency"] = Math.Min(consecutiveDays * 2m, 9m);

			decimal totalScore = scores.Values.Sum();

			var breakdown = string.Join(" | ",
				scores.Select(kv => $"{kv.Key}:{kv.Value:F0}")) +
				$" | TOTAL: {totalScore:F0}/100 ({totalScore / 100m:P0})";

			return (totalScore / 100m, breakdown);
		}
	}

}