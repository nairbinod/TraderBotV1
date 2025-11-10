using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Simplified Quality Score Calculator
	/// Less restrictive than the original 7-factor system
	/// Focuses on 4 core quality factors with more lenient thresholds
	/// </summary>
	public static class SimplifiedQualityScore
	{
		/// <summary>
		/// Calculate a simplified quality score (0-1)
		/// More lenient than the original scoring system
		/// </summary>
		public static decimal Calculate(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 30)
				return 0m;

			int idx = closes.Count - 1;
			decimal score = 0m;

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 1: TREND ALIGNMENT (30 points)
			// Check if signal aligns with current trend
			// ═══════════════════════════════════════════════════════════════

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
				{
					// Perfect alignment with uptrend
					score += 30m * Math.Min(context.TrendStrength / 0.02m, 1m);
				}
				else if (!context.IsDowntrend)
				{
					// Neutral market is acceptable for counter-trend
					score += 18m;
				}
				else
				{
					// Counter-trend in downtrend (mean reversion play)
					score += 10m;
				}
			}
			else if (proposedDirection == "Sell")
			{
				if (context.IsDowntrend)
				{
					score += 30m * Math.Min(context.TrendStrength / 0.02m, 1m);
				}
				else if (!context.IsUptrend)
				{
					score += 18m;
				}
				else
				{
					score += 10m;
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 2: PRICE ACTION QUALITY (25 points)
			// Recent price movement and momentum
			// ═══════════════════════════════════════════════════════════════

			decimal priceChange = closes[idx] - closes[idx - 1];
			decimal percentChange = priceChange / closes[idx - 1];

			bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
								 (proposedDirection == "Sell" && priceChange < 0);

			if (directionMatch)
			{
				// Signal direction matches recent price action (momentum continuation)
				decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.02m, 1m);
				score += 25m * moveStrength;
			}
			else
			{
				// Counter-trend setup (mean reversion)
				// Strong counter-moves can indicate good reversal opportunities
				if (Math.Abs(percentChange) > 0.015m)
				{
					score += 18m;  // Substantial counter-move = potential reversal
				}
				else if (Math.Abs(percentChange) > 0.008m)
				{
					score += 12m;  // Moderate counter-move
				}
				else
				{
					score += 6m;   // Small counter-move
				}
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 3: VOLUME CONFIRMATION (25 points)
			// Higher volume on the signal bar is better
			// ═══════════════════════════════════════════════════════════════

			if (volumes.Count > idx && idx >= 20)
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
				decimal avgVolume = recentVolumes.Average();

				if (avgVolume > 0)
				{
					decimal volumeRatio = volumes[idx] / avgVolume;

					if (volumeRatio > 1.3m)
					{
						// Strong volume spike (excellent)
						score += 25m * Math.Min((volumeRatio - 1.3m) / 1.2m, 1m);
					}
					else if (volumeRatio > 1.1m)
					{
						// Above average volume (good)
						score += 18m;
					}
					else if (volumeRatio > 0.8m)
					{
						// Average volume (acceptable)
						score += 13m;
					}
					else
					{
						// Below average volume (weak but not disqualifying)
						score += 8m;
					}
				}
				else
				{
					score += 13m;  // Default if can't calculate
				}
			}
			else
			{
				// No volume data available - give partial credit
				score += 13m;
			}

			// ═══════════════════════════════════════════════════════════════
			// FACTOR 4: RSI SANITY CHECK (20 points)
			// Ensure RSI is in reasonable range
			// ═══════════════════════════════════════════════════════════════

			var rsiList = Indicators.RSIList(closes, 14);

			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 35m && rsiValue <= 70m)
					{
						// Ideal range for buying
						score += 20m;
					}
					else if (rsiValue < 35m)
					{
						// Oversold can be bullish (mean reversion)
						score += 17m;
					}
					else if (rsiValue <= 80m)
					{
						// Slightly overbought but might continue
						score += 10m;
					}
					else
					{
						// Very overbought - questionable
						score += 5m;
					}
				}
				else if (proposedDirection == "Sell")
				{
					if (rsiValue >= 30m && rsiValue <= 65m)
					{
						// Ideal range for selling
						score += 20m;
					}
					else if (rsiValue > 65m)
					{
						// Overbought can be bearish
						score += 17m;
					}
					else if (rsiValue >= 20m)
					{
						// Slightly oversold but might continue
						score += 10m;
					}
					else
					{
						// Very oversold - questionable
						score += 5m;
					}
				}
			}
			else
			{
				// No RSI available - give average score
				score += 12m;
			}

			// ═══════════════════════════════════════════════════════════════
			// BASELINE GUARANTEE
			// Ensure every signal gets minimum score
			// ═══════════════════════════════════════════════════════════════

			const decimal MINIMUM_SCORE = 35m;  // Guarantee at least 35%
			if (score < MINIMUM_SCORE)
			{
				score = MINIMUM_SCORE;
			}

			// Normalize to 0-1 range
			return Math.Min(score / 100m, 1m);
		}

		/// <summary>
		/// Calculate quality score with detailed breakdown
		/// Returns score and explanation of each factor
		/// </summary>
		public static (decimal score, string breakdown) CalculateWithDetails(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 30)
				return (0m, "Insufficient data");

			int idx = closes.Count - 1;
			var breakdown = new List<string>();
			decimal trendScore = 0m;
			decimal priceScore = 0m;
			decimal volumeScore = 0m;
			decimal rsiScore = 0m;

			// Factor 1: Trend
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			if (proposedDirection == "Buy")
			{
				if (context.IsUptrend)
				{
					trendScore = 30m * Math.Min(context.TrendStrength / 0.02m, 1m);
					breakdown.Add($"Trend: {trendScore:F1}/30 (Uptrend aligned)");
				}
				else if (!context.IsDowntrend)
				{
					trendScore = 18m;
					breakdown.Add($"Trend: {trendScore:F1}/30 (Neutral market)");
				}
				else
				{
					trendScore = 10m;
					breakdown.Add($"Trend: {trendScore:F1}/30 (Counter-trend)");
				}
			}
			else
			{
				if (context.IsDowntrend)
				{
					trendScore = 30m * Math.Min(context.TrendStrength / 0.02m, 1m);
					breakdown.Add($"Trend: {trendScore:F1}/30 (Downtrend aligned)");
				}
				else if (!context.IsUptrend)
				{
					trendScore = 18m;
					breakdown.Add($"Trend: {trendScore:F1}/30 (Neutral market)");
				}
				else
				{
					trendScore = 10m;
					breakdown.Add($"Trend: {trendScore:F1}/30 (Counter-trend)");
				}
			}

			// Factor 2: Price Action
			decimal priceChange = closes[idx] - closes[idx - 1];
			decimal percentChange = priceChange / closes[idx - 1];
			bool directionMatch = (proposedDirection == "Buy" && priceChange > 0) ||
								 (proposedDirection == "Sell" && priceChange < 0);

			if (directionMatch)
			{
				decimal moveStrength = Math.Min(Math.Abs(percentChange) / 0.02m, 1m);
				priceScore = 25m * moveStrength;
				breakdown.Add($"Price: {priceScore:F1}/25 (Momentum continuation, {percentChange:P2})");
			}
			else
			{
				if (Math.Abs(percentChange) > 0.015m)
					priceScore = 18m;
				else if (Math.Abs(percentChange) > 0.008m)
					priceScore = 12m;
				else
					priceScore = 6m;
				breakdown.Add($"Price: {priceScore:F1}/25 (Mean reversion, {percentChange:P2})");
			}

			// Factor 3: Volume
			if (volumes.Count > idx && idx >= 20)
			{
				var recentVolumes = volumes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
				decimal avgVolume = recentVolumes.Average();
				decimal volumeRatio = avgVolume > 0 ? volumes[idx] / avgVolume : 1m;

				if (volumeRatio > 1.3m)
					volumeScore = 25m * Math.Min((volumeRatio - 1.3m) / 1.2m, 1m);
				else if (volumeRatio > 1.1m)
					volumeScore = 18m;
				else if (volumeRatio > 0.8m)
					volumeScore = 13m;
				else
					volumeScore = 8m;

				breakdown.Add($"Volume: {volumeScore:F1}/25 ({volumeRatio:F2}x average)");
			}
			else
			{
				volumeScore = 13m;
				breakdown.Add($"Volume: {volumeScore:F1}/25 (No data - default)");
			}

			// Factor 4: RSI
			var rsiList = Indicators.RSIList(closes, 14);
			if (rsiList.Count > 0)
			{
				decimal rsiValue = rsiList[^1];

				if (proposedDirection == "Buy")
				{
					if (rsiValue >= 35m && rsiValue <= 70m)
						rsiScore = 20m;
					else if (rsiValue < 35m)
						rsiScore = 17m;
					else if (rsiValue <= 80m)
						rsiScore = 10m;
					else
						rsiScore = 5m;
				}
				else
				{
					if (rsiValue >= 30m && rsiValue <= 65m)
						rsiScore = 20m;
					else if (rsiValue > 65m)
						rsiScore = 17m;
					else if (rsiValue >= 20m)
						rsiScore = 10m;
					else
						rsiScore = 5m;
				}

				breakdown.Add($"RSI: {rsiScore:F1}/20 (RSI={rsiValue:F1})");
			}
			else
			{
				rsiScore = 12m;
				breakdown.Add($"RSI: {rsiScore:F1}/20 (No data - default)");
			}

			// Calculate total
			decimal totalScore = trendScore + priceScore + volumeScore + rsiScore;

			// Apply minimum
			const decimal MINIMUM_SCORE = 35m;
			if (totalScore < MINIMUM_SCORE)
			{
				breakdown.Add($"Baseline adjustment: +{MINIMUM_SCORE - totalScore:F1} (minimum guarantee)");
				totalScore = MINIMUM_SCORE;
			}

			decimal normalizedScore = Math.Min(totalScore / 100m, 1m);

			string breakdownStr = string.Join(" | ", breakdown) + $" | TOTAL: {normalizedScore:P0}";

			return (normalizedScore, breakdownStr);
		}
	}
}