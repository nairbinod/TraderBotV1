using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// IMPROVED SWING TRADING QUALITY SCORE CALCULATOR
	/// Optimized for 1-2 week holding periods on DAILY bars
	/// 
	/// Key Improvements Over Original:
	/// 1. 7-day momentum (not 3-day) for swing timeframe
	/// 2. Weekly momentum factor (25-day lookback)
	/// 3. Fixed momentum persistence logic (doesn't break early)
	/// 4. Stricter below-average volume penalties
	/// 5. Higher trend strength target (2% not 1.2%)
	/// 6. Graduated RSI scoring (favors middle of range)
	/// 7. Price action quality check (whipsaws & gaps)
	/// 8. Volatility expansion/contraction awareness
	/// 9. Better S/R distance penalties
	/// </summary>
	public static class SwingTradingQualityScore
	{
		/// <summary>
		/// Calculate improved swing trading quality score (0-1 scale)
		/// </summary>
		public static decimal Calculate(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 60)
				return 0m;

			int idx = closes.Count - 1;
			decimal score = 0m;

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 1: TREND STRENGTH & DIRECTION (20 points)
			// ⭐ IMPROVED: Higher target (2% not 1.2%) for swing trading
			// ═══════════════════════════════════════════════════════════════
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					score += 20m * Math.Min(context.TrendStrength / 0.020m, 1m);  // ⭐ 2.0% target
				else if (!context.IsDowntrend)
					score += 10m;  // Neutral okay
				else
					score += 2m;   // Counter-trend risky
			}
			else if (proposedDirection == "Sell")
			{
				if (context.IsDowntrend)
					score += 20m * Math.Min(context.TrendStrength / 0.020m, 1m);
				else if (!context.IsUptrend)
					score += 10m;
				else
					score += 2m;
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 2: WEEKLY MOMENTUM (12 points) - NEW!
			// ⭐ Check alignment with 5-week (25-day) trend
			// ═══════════════════════════════════════════════════════════════
			if (idx >= 25)
			{
				decimal price5WeeksAgo = closes[idx - 25];
				decimal weeklyMove = (closes[idx] - price5WeeksAgo) / price5WeeksAgo;

				bool weeklyAligned = (proposedDirection == "Buy" && weeklyMove > 0) ||
									(proposedDirection == "Sell" && weeklyMove < 0);

				if (weeklyAligned)
				{
					// Scale by move strength (10% target for 5 weeks)
					decimal moveStrength = Math.Min(Math.Abs(weeklyMove) / 0.10m, 1m);
					score += 12m * moveStrength;
				}
				else
				{
					score += 3m;  // Counter to weekly trend
				}
			}
			else
			{
				score += 6m;  // Not enough history
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 3: MULTI-DAY MOMENTUM (15 points)
			// ⭐ IMPROVED: 7-day AND 3-day (not just 3-day)
			// ═══════════════════════════════════════════════════════════════
			if (idx >= 7)
			{
				decimal price7DaysAgo = closes[idx - 7];
				decimal price3DaysAgo = closes[idx - 3];

				decimal move7D = (closes[idx] - price7DaysAgo) / price7DaysAgo;
				decimal move3D = (closes[idx] - price3DaysAgo) / price3DaysAgo;

				bool momentum7DMatch = (proposedDirection == "Buy" && move7D > 0) ||
									  (proposedDirection == "Sell" && move7D < 0);
				bool momentum3DMatch = (proposedDirection == "Buy" && move3D > 0) ||
									  (proposedDirection == "Sell" && move3D < 0);

				// Both timeframes aligned = full points
				if (momentum7DMatch && momentum3DMatch)
				{
					// Scale by 7-day move strength (5% target)
					decimal moveStrength = Math.Min(Math.Abs(move7D) / 0.05m, 1m);
					score += 15m * moveStrength;
				}
				// Only 7-day aligned (entry timing off)
				else if (momentum7DMatch)
				{
					decimal moveStrength = Math.Min(Math.Abs(move7D) / 0.05m, 1m);
					score += 10m * moveStrength;
				}
				// Counter-trend setup
				else
				{
					// Mean reversion needs strong counter-move
					decimal counterStrength = Math.Max(Math.Abs(move7D), Math.Abs(move3D));
					if (counterStrength > 0.07m)  // >7% move
						score += 10m;
					else if (counterStrength > 0.05m)
						score += 6m;
					else
						score += 3m;
				}
			}
			else
			{
				score += 7m;  // Not enough history
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 4: VOLUME STRENGTH (12 points)
			// ⭐ IMPROVED: Stricter penalties for below-average volume
			// ═══════════════════════════════════════════════════════════════
			if (volumes.Count > idx && idx >= 30)
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 30)).Take(30).ToList();
				decimal avgVolume = recentVolumes.Average();

				if (avgVolume > 0)
				{
					decimal volumeRatio = volumes[idx] / avgVolume;

					if (volumeRatio > 1.8m)        // Very strong
						score += 12m;
					else if (volumeRatio > 1.5m)   // Strong
						score += 11m;
					else if (volumeRatio > 1.2m)   // Good
						score += 9m;
					else if (volumeRatio > 1.0m)   // Average
						score += 7m;
					else if (volumeRatio > 0.8m)   // ⭐ Below average (was 8, now 4)
						score += 4m;
					else                           // Weak
						score += 2m;
				}
				else
				{
					score += 6m;  // Invalid data
				}
			}
			else
			{
				score += 6m;  // No volume data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 5: VOLATILITY & EXPANSION (10 points)
			// ⭐ IMPROVED: Checks level AND expansion/contraction
			// ═══════════════════════════════════════════════════════════════
			var atr = Indicators.ATRList(highs, lows, closes, 20);  // ⭐ Use 20-period for swing
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal price = closes[idx];
				decimal volatilityPct = (atr[^1] / price) * 100m;

				// Check volatility level (7 points)
				if (volatilityPct >= 1.5m && volatilityPct <= 4.5m)
					score += 7m;  // Ideal range
				else if (volatilityPct >= 1.0m && volatilityPct <= 6.0m)
					score += 5m;  // Acceptable
				else if (volatilityPct >= 0.7m && volatilityPct <= 7.0m)
					score += 3m;  // Marginal
				else
					score += 1m;  // Too extreme

				// Check volatility trend (3 points) - NEW!
				if (idx >= 10)
				{
					var atr10DaysAgo = Indicators.ATRList(highs, lows, closes, 20);
					if (atr.Count > 10)
					{
						decimal atr10Ago = atr[atr.Count - 11];  // 10 days ago
						decimal atrChange = (atr[^1] - atr10Ago) / atr10Ago;

						// Expanding volatility good for trend-following
						if (proposedDirection == "Buy" && atrChange > 0.15m)      // Expanding 15%+
							score += 3m;
						else if (proposedDirection == "Sell" && atrChange < -0.15m)  // Contracting 15%+
							score += 3m;
						else if (Math.Abs(atrChange) < 0.10m)  // Stable
							score += 2m;
						else
							score += 1m;  // Neutral
					}
				}
			}
			else
			{
				score += 5m;  // No ATR data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 6: RSI POSITION (10 points)
			// ⭐ IMPROVED: Graduated scoring (favors middle of range)
			// ═══════════════════════════════════════════════════════════════
			var rsiList = Indicators.RSIList(closes, 14);
			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 40m && rsiValue <= 55m)
						score += 10m;  // ⭐ Perfect zone (tightened)
					else if (rsiValue >= 35m && rsiValue <= 60m)
						score += 8m;   // Good zone
					else if (rsiValue >= 30m && rsiValue <= 65m)
						score += 6m;   // Acceptable
					else if (rsiValue < 30m)
						score += 7m;   // Oversold bounce play
					else
						score += 2m;   // Too overbought
				}
				else if (proposedDirection == "Sell")
				{
					if (rsiValue >= 45m && rsiValue <= 60m)
						score += 10m;  // ⭐ Perfect zone
					else if (rsiValue >= 40m && rsiValue <= 65m)
						score += 8m;   // Good zone
					else if (rsiValue >= 35m && rsiValue <= 70m)
						score += 6m;   // Acceptable
					else if (rsiValue > 70m)
						score += 7m;   // Overbought reversal play
					else
						score += 2m;   // Too oversold
				}
			}
			else
			{
				score += 5m;  // No RSI data
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 7: SUPPORT/RESISTANCE PROXIMITY (10 points)
			// ⭐ IMPROVED: Stricter penalties for poor entry positioning
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

					// ⭐ More graduated penalties
					if (distanceToSupport < 0.02m)       // Within 2%
						score += 10m;
					else if (distanceToSupport < 0.04m)  // Within 4%
						score += 8m;
					else if (distanceToSupport < 0.06m)  // Within 6%
						score += 5m;
					else if (distanceToSupport < 0.08m)  // Within 8%
						score += 3m;
					else
						score += 2m;  // ⭐ Far from support (was 6, now 2)
				}
				else
				{
					score += 5m;  // No S/R found
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

					if (distanceToResistance < 0.02m)
						score += 10m;
					else if (distanceToResistance < 0.04m)
						score += 8m;
					else if (distanceToResistance < 0.06m)
						score += 5m;
					else if (distanceToResistance < 0.08m)
						score += 3m;
					else
						score += 2m;
				}
				else
				{
					score += 5m;
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 8: MOMENTUM PERSISTENCE (8 points)
			// ⭐ FIXED: Counts all days, doesn't break early
			// ═══════════════════════════════════════════════════════════════
			int daysInDirection = 0;
			int totalDays = 7;  // ⭐ Check 7 days (was 5)

			for (int i = idx; i >= Math.Max(1, idx - totalDays); i--)
			{
				if (i > 0)
				{
					bool upDay = closes[i] > closes[i - 1];
					bool matchesDirection = (proposedDirection == "Buy" && upDay) ||
										   (proposedDirection == "Sell" && !upDay);

					if (matchesDirection)
						daysInDirection++;
					// ⭐ DON'T BREAK - count all matching days
				}
			}

			// Need 5/7 days (71%) for full points
			decimal persistenceRatio = daysInDirection / (decimal)totalDays;
			score += persistenceRatio * 8m;

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 9: PRICE ACTION QUALITY (3 points) - NEW!
			// ⭐ Checks for clean vs choppy movement
			// ═══════════════════════════════════════════════════════════════
			int whipsaws = 0;
			int largeGaps = 0;

			// Count direction changes (whipsaws) over 10 days
			for (int i = idx; i >= Math.Max(2, idx - 10); i--)
			{
				if (i > 1)
				{
					bool prevUp = closes[i - 1] > closes[i - 2];
					bool currUp = closes[i] > closes[i - 1];
					if (prevUp != currUp)
						whipsaws++;
				}
			}

			// Count large gaps (>3%) over 7 days
			for (int i = idx; i >= Math.Max(1, idx - 7); i--)
			{
				if (i > 0)
				{
					decimal gap = Math.Abs(closes[i] - closes[i - 1]) / closes[i - 1];
					if (gap > 0.03m)  // >3% gap
						largeGaps++;
				}
			}

			// Score based on cleanliness
			if (whipsaws <= 4 && largeGaps <= 1)
				score += 3m;  // Clean price action
			else if (whipsaws <= 6 && largeGaps <= 2)
				score += 2m;  // Acceptable
			else
				score += 1m;  // Choppy

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

			// Factor 1: Trend (20 pts)
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
					scores["Trend"] = 20m * Math.Min(context.TrendStrength / 0.020m, 1m);
				else if (!context.IsDowntrend)
					scores["Trend"] = 10m;
				else
					scores["Trend"] = 2m;
			}
			else
			{
				if (context.IsDowntrend)
					scores["Trend"] = 20m * Math.Min(context.TrendStrength / 0.020m, 1m);
				else if (!context.IsUptrend)
					scores["Trend"] = 10m;
				else
					scores["Trend"] = 2m;
			}

			// Factor 2: Weekly Momentum (12 pts)
			if (idx >= 25)
			{
				decimal price5WeeksAgo = closes[idx - 25];
				decimal weeklyMove = (closes[idx] - price5WeeksAgo) / price5WeeksAgo;
				bool weeklyAligned = (proposedDirection == "Buy" && weeklyMove > 0) ||
									(proposedDirection == "Sell" && weeklyMove < 0);

				if (weeklyAligned)
				{
					decimal moveStrength = Math.Min(Math.Abs(weeklyMove) / 0.10m, 1m);
					scores["Weekly"] = 12m * moveStrength;
				}
				else
					scores["Weekly"] = 3m;
			}
			else
				scores["Weekly"] = 6m;

			// Factor 3: Multi-Day Momentum (15 pts)
			if (idx >= 7)
			{
				decimal move7D = (closes[idx] - closes[idx - 7]) / closes[idx - 7];
				decimal move3D = (closes[idx] - closes[idx - 3]) / closes[idx - 3];

				bool mom7D = (proposedDirection == "Buy" && move7D > 0) ||
							(proposedDirection == "Sell" && move7D < 0);
				bool mom3D = (proposedDirection == "Buy" && move3D > 0) ||
							(proposedDirection == "Sell" && move3D < 0);

				if (mom7D && mom3D)
				{
					decimal moveStrength = Math.Min(Math.Abs(move7D) / 0.05m, 1m);
					scores["Momentum"] = 15m * moveStrength;
				}
				else if (mom7D)
				{
					decimal moveStrength = Math.Min(Math.Abs(move7D) / 0.05m, 1m);
					scores["Momentum"] = 10m * moveStrength;
				}
				else
					scores["Momentum"] = 3m;
			}
			else
				scores["Momentum"] = 7m;

			// Factor 4: Volume (12 pts)
			if (volumes.Count > idx && idx >= 30)
			{
				var recentVols = volumes.Skip(Math.Max(0, idx - 30)).Take(30).ToList();
				decimal avgVol = recentVols.Average();
				if (avgVol > 0)
				{
					decimal ratio = volumes[idx] / avgVol;
					scores["Volume"] = ratio > 1.8m ? 12m :
									  ratio > 1.5m ? 11m :
									  ratio > 1.2m ? 9m :
									  ratio > 1.0m ? 7m :
									  ratio > 0.8m ? 4m : 2m;
				}
				else
					scores["Volume"] = 6m;
			}
			else
				scores["Volume"] = 6m;

			// Factor 5: Volatility (10 pts)
			var atr = Indicators.ATRList(highs, lows, closes, 20);
			if (atr.Count > 0 && atr[^1] > 0)
			{
				decimal volPct = (atr[^1] / closes[idx]) * 100m;
				decimal levelScore = volPct >= 1.5m && volPct <= 4.5m ? 7m :
									volPct >= 1.0m && volPct <= 6.0m ? 5m :
									volPct >= 0.7m && volPct <= 7.0m ? 3m : 1m;

				decimal expansionScore = 0m;
				if (idx >= 10 && atr.Count > 10)
				{
					decimal atr10Ago = atr[atr.Count - 11];
					decimal atrChange = (atr[^1] - atr10Ago) / atr10Ago;
					if ((proposedDirection == "Buy" && atrChange > 0.15m) ||
						(proposedDirection == "Sell" && atrChange < -0.15m))
						expansionScore = 3m;
					else if (Math.Abs(atrChange) < 0.10m)
						expansionScore = 2m;
					else
						expansionScore = 1m;
				}
				scores["Volatility"] = levelScore + expansionScore;
			}
			else
				scores["Volatility"] = 5m;

			// Factor 6: RSI (10 pts)
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > 0)
			{
				decimal rsiVal = rsi[^1];
				if (proposedDirection == "Buy")
				{
					scores["RSI"] = rsiVal >= 40m && rsiVal <= 55m ? 10m :
								   rsiVal >= 35m && rsiVal <= 60m ? 8m :
								   rsiVal >= 30m && rsiVal <= 65m ? 6m :
								   rsiVal < 30m ? 7m : 2m;
				}
				else
				{
					scores["RSI"] = rsiVal >= 45m && rsiVal <= 60m ? 10m :
								   rsiVal >= 40m && rsiVal <= 65m ? 8m :
								   rsiVal >= 35m && rsiVal <= 70m ? 6m :
								   rsiVal > 70m ? 7m : 2m;
				}
			}
			else
				scores["RSI"] = 5m;

			// Factor 7: S/R (10 pts)
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
					scores["S/R"] = dist < 0.02m ? 10m :
								   dist < 0.04m ? 8m :
								   dist < 0.06m ? 5m :
								   dist < 0.08m ? 3m : 2m;
				}
				else
					scores["S/R"] = 5m;
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
					scores["S/R"] = dist < 0.02m ? 10m :
								   dist < 0.04m ? 8m :
								   dist < 0.06m ? 5m :
								   dist < 0.08m ? 3m : 2m;
				}
				else
					scores["S/R"] = 5m;
			}

			// Factor 8: Persistence (8 pts)
			int daysInDirection = 0;
			int totalDays = 7;
			for (int i = idx; i >= Math.Max(1, idx - totalDays); i--)
			{
				if (i > 0)
				{
					bool upDay = closes[i] > closes[i - 1];
					bool matchesDirection = (proposedDirection == "Buy" && upDay) ||
										   (proposedDirection == "Sell" && !upDay);
					if (matchesDirection)
						daysInDirection++;
				}
			}
			scores["Persistence"] = (daysInDirection / (decimal)totalDays) * 8m;

			// Factor 9: Quality (3 pts)
			int whipsaws = 0;
			int largeGaps = 0;
			for (int i = idx; i >= Math.Max(2, idx - 10); i--)
			{
				if (i > 1)
				{
					bool prevUp = closes[i - 1] > closes[i - 2];
					bool currUp = closes[i] > closes[i - 1];
					if (prevUp != currUp) whipsaws++;
				}
			}
			for (int i = idx; i >= Math.Max(1, idx - 7); i--)
			{
				if (i > 0)
				{
					decimal gap = Math.Abs(closes[i] - closes[i - 1]) / closes[i - 1];
					if (gap > 0.03m) largeGaps++;
				}
			}
			scores["Quality"] = whipsaws <= 4 && largeGaps <= 1 ? 3m :
							   whipsaws <= 6 && largeGaps <= 2 ? 2m : 1m;

			decimal totalScore = scores.Values.Sum();

			var breakdown = string.Join(" | ",
				scores.Select(kv => $"{kv.Key}:{kv.Value:F1}")) +
				$" | TOTAL: {totalScore:F0}/100 ({totalScore / 100m:P0})";

			return (totalScore / 100m, breakdown);
		}
	}
}