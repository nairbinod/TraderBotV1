using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Enhanced trading strategies with improved signal quality
	/// Focuses on reducing false signals and improving win rate
	/// </summary>
	public static class StrategiesEnhanced
	{
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));
		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);

		// ═══════════════════════════════════════════════════════════════
		// 1) TREND FOLLOWING WITH MULTI-TIMEFRAME CONFIRMATION
		// ═══════════════════════════════════════════════════════════════

		public static StrategySignal TrendFollowingMTF(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			if (closes.Count < 200)
				return Hold("Insufficient data for MTF analysis");

			// Detect market regime
			var regime = IndicatorsEnhanced.DetectMarketRegime(closes, highs, lows);

			// Only trade in trending markets
			if (regime.Regime == IndicatorsEnhanced.MarketRegime.Ranging ||
				regime.Regime == IndicatorsEnhanced.MarketRegime.Quiet)
			{
				return Hold($"Market is {regime.Description} - avoid trend following");
			}

			// Multi-timeframe analysis
			var mtf = IndicatorsEnhanced.AnalyzeMultiTimeframe(closes, highs, lows);

			// Require timeframe alignment
			if (!mtf.IsAligned)
			{
				return Hold($"Timeframes not aligned: {mtf.Reason}");
			}

			// Calculate moving averages
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = Indicators.EMAList(closes, 200);

			int idx = closes.Count - 1;
			decimal price = closes[idx];

			// Check for pullback to moving average (better entry)
			bool pullbackToEMA = false;
			if (mtf.CurrentTFTrend == "Up")
			{
				// Look for pullback to EMA20 or EMA50 in uptrend
				pullbackToEMA = (price >= ema20[idx] * 0.995m && price <= ema20[idx] * 1.01m) ||
							   (price >= ema50[idx] * 0.99m && price <= ema50[idx] * 1.02m);
			}
			else if (mtf.CurrentTFTrend == "Down")
			{
				pullbackToEMA = (price <= ema20[idx] * 1.005m && price >= ema20[idx] * 0.99m) ||
							   (price <= ema50[idx] * 1.01m && price >= ema50[idx] * 0.98m);
			}

			// RSI confirmation (not overbought/oversold)
			var rsi = Indicators.RSIList(closes, 14);
			decimal rsiValue = rsi.Count > idx ? rsi[idx] : 50m;

			if (mtf.CurrentTFTrend == "Up")
			{
				// Buy signal
				if (regime.IsTrendingUp && price > ema200[idx])
				{
					// Better entry on pullback
					if (pullbackToEMA && rsiValue > 40m && rsiValue < 70m)
					{
						decimal strength = Clamp01(mtf.Confidence * 0.5m + regime.RegimeConfidence * 0.3m + 0.2m);
						return new("Buy", strength,
							$"MTF uptrend with pullback entry (RSI={rsiValue:F1}, regime={regime.Description})");
					}

					// Standard entry
					if (rsiValue < 70m)
					{
						decimal strength = Clamp01(mtf.Confidence * 0.4m + regime.RegimeConfidence * 0.3m);
						return new("Buy", strength,
							$"MTF uptrend confirmed (RSI={rsiValue:F1})");
					}
				}
			}
			else if (mtf.CurrentTFTrend == "Down")
			{
				// Sell signal
				if (regime.IsTrendingDown && price < ema200[idx])
				{
					if (pullbackToEMA && rsiValue < 60m && rsiValue > 30m)
					{
						decimal strength = Clamp01(mtf.Confidence * 0.5m + regime.RegimeConfidence * 0.3m + 0.2m);
						return new("Sell", strength,
							$"MTF downtrend with pullback entry (RSI={rsiValue:F1}, regime={regime.Description})");
					}

					if (rsiValue > 30m)
					{
						decimal strength = Clamp01(mtf.Confidence * 0.4m + regime.RegimeConfidence * 0.3m);
						return new("Sell", strength,
							$"MTF downtrend confirmed (RSI={rsiValue:F1})");
					}
				}
			}

			return Hold($"No high-quality MTF setup (trend={mtf.CurrentTFTrend}, RSI={rsiValue:F1})");
		}

		// ═══════════════════════════════════════════════════════════════
		// 2) MEAN REVERSION WITH SUPPORT/RESISTANCE
		// ═══════════════════════════════════════════════════════════════

		public static StrategySignal MeanReversionSR(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (closes.Count < 100)
				return Hold("Insufficient data");

			// Only trade mean reversion in ranging markets
			var regime = IndicatorsEnhanced.DetectMarketRegime(closes, highs, lows);

			if (regime.Regime == IndicatorsEnhanced.MarketRegime.StrongTrending)
			{
				return Hold("Strong trend - avoid mean reversion");
			}

			// Find support/resistance levels
			var srLevels = IndicatorsEnhanced.FindSupportResistance(highs, lows, closes, 50, 0.015m);

			if (srLevels.Count == 0)
				return Hold("No clear S/R levels");

			int idx = closes.Count - 1;
			decimal price = closes[idx];

			// Calculate Bollinger Bands
			var (bbUpper, bbMiddle, bbLower) = Indicators.BollingerBandsFast(closes, 20, 2m);

			if (bbUpper[idx] == null || bbLower[idx] == null || bbMiddle[idx] == null)
				return Hold("BB not ready");

			// RSI for overbought/oversold
			var rsi = Indicators.RSIList(closes, 14);
			decimal rsiValue = rsi.Count > idx ? rsi[idx] : 50m;

			// Volume analysis
			var volAnalysis = IndicatorsEnhanced.AnalyzeVolume(closes, volumes, 20);

			// Find nearest support
			var nearestSupport = srLevels
				.Where(l => l.IsSupport && l.Level < price)
				.OrderByDescending(l => l.Level)
				.FirstOrDefault();

			// Find nearest resistance
			var nearestResistance = srLevels
				.Where(l => !l.IsSupport && l.Level > price)
				.OrderBy(l => l.Level)
				.FirstOrDefault();

			// BUY at support
			if (nearestSupport != null)
			{
				decimal distanceToSupport = (price - nearestSupport.Level) / price;

				// Price near support, oversold, and at lower Bollinger Band
				if (distanceToSupport < 0.02m &&
					rsiValue < 35m &&
					price <= bbLower[idx] * 1.005m &&
					volAnalysis.IsAccumulation)
				{
					decimal strength = Clamp01(
						nearestSupport.Strength * 0.4m +
						(35m - rsiValue) / 35m * 0.3m +
						volAnalysis.VolumeStrength * 0.3m
					);

					return new("Buy", strength,
						$"Mean reversion at support ${nearestSupport.Level:F2} (RSI={rsiValue:F1}, touches={nearestSupport.Touches})");
				}
			}

			// SELL at resistance
			if (nearestResistance != null)
			{
				decimal distanceToResistance = (nearestResistance.Level - price) / price;

				if (distanceToResistance < 0.02m &&
					rsiValue > 65m &&
					price >= bbUpper[idx] * 0.995m &&
					volAnalysis.IsDistribution)
				{
					decimal strength = Clamp01(
						nearestResistance.Strength * 0.4m +
						(rsiValue - 65m) / 35m * 0.3m +
						volAnalysis.VolumeStrength * 0.3m
					);

					return new("Sell", strength,
						$"Mean reversion at resistance ${nearestResistance.Level:F2} (RSI={rsiValue:F1}, touches={nearestResistance.Touches})");
				}
			}

			return Hold($"No mean reversion setup (RSI={rsiValue:F1}, regime={regime.Description})");
		}

		// ═══════════════════════════════════════════════════════════════
		// 3) BREAKOUT WITH VOLUME CONFIRMATION
		// ═══════════════════════════════════════════════════════════════

		public static StrategySignal BreakoutWithVolume(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (closes.Count < 60 || volumes == null || volumes.Count != closes.Count)
				return Hold("Insufficient data");

			// Detect consolidation using ADX
			var (adx, diPlus, diMinus) = IndicatorsExtended.ADXList(highs, lows, closes, 14);
			int idx = closes.Count - 1;

			if (adx.Count <= idx)
				return Hold("ADX not ready");

			decimal adxValue = adx[idx];

			// Look for consolidation (low ADX) followed by expansion
			bool wasConsolidating = adx.Count > idx + 1 && adx[idx - 5] < 20m;
			bool isBreaking = adxValue > adx[idx - 1] && adxValue > adx[idx - 2];

			if (!wasConsolidating || !isBreaking)
				return Hold($"No breakout pattern (ADX={adxValue:F1})");

			// Find consolidation range
			var recentPrices = closes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
			decimal consolidationHigh = recentPrices.Max();
			decimal consolidationLow = recentPrices.Min();
			decimal consolidationRange = consolidationHigh - consolidationLow;

			decimal price = closes[idx];

			// Volume confirmation
			var volAnalysis = IndicatorsEnhanced.AnalyzeVolume(closes, volumes, 20);

			if (volAnalysis.VolumeRatio < 1.5m)
				return Hold($"Insufficient volume for breakout (ratio={volAnalysis.VolumeRatio:F2})");

			// Candlestick pattern confirmation
			var patterns = IndicatorsEnhanced.RecognizePatterns(opens, highs, lows, closes);
			bool hasBullishPattern = patterns.Any(p => p.Signal == "Bullish" && p.Strength > 0.6m);
			bool hasBearishPattern = patterns.Any(p => p.Signal == "Bearish" && p.Strength > 0.6m);

			// Bullish breakout
			if (price > consolidationHigh &&
				closes[idx - 1] <= consolidationHigh &&
				volAnalysis.IsAccumulation &&
				diPlus[idx] > diMinus[idx])
			{
				decimal breakoutStrength = (price - consolidationHigh) / consolidationRange;
				decimal strength = Clamp01(
					breakoutStrength * 0.3m +
					volAnalysis.VolumeStrength * 0.4m +
					(hasBullishPattern ? 0.3m : 0.1m)
				);

				string patternInfo = hasBullishPattern ?
					$" + {patterns.First(p => p.Signal == "Bullish").PatternName}" : "";

				return new("Buy", strength,
					$"Bullish breakout above ${consolidationHigh:F2} with {volAnalysis.VolumeRatio:F1}x volume{patternInfo}");
			}

			// Bearish breakdown
			if (price < consolidationLow &&
				closes[idx - 1] >= consolidationLow &&
				volAnalysis.IsDistribution &&
				diMinus[idx] > diPlus[idx])
			{
				decimal breakdownStrength = (consolidationLow - price) / consolidationRange;
				decimal strength = Clamp01(
					breakdownStrength * 0.3m +
					volAnalysis.VolumeStrength * 0.4m +
					(hasBearishPattern ? 0.3m : 0.1m)
				);

				string patternInfo = hasBearishPattern ?
					$" + {patterns.First(p => p.Signal == "Bearish").PatternName}" : "";

				return new("Sell", strength,
					$"Bearish breakdown below ${consolidationLow:F2} with {volAnalysis.VolumeRatio:F1}x volume{patternInfo}");
			}

			return Hold("Breakout conditions not met");
		}

		// ═══════════════════════════════════════════════════════════════
		// 4) MOMENTUM REVERSAL WITH DIVERGENCE
		// ═══════════════════════════════════════════════════════════════

		public static StrategySignal MomentumReversalDivergence(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (closes.Count < 100)
				return Hold("Insufficient data");

			int idx = closes.Count - 1;

			// Calculate RSI and MACD
			var rsi = Indicators.RSIList(closes, 14);
			var (macd, signal, hist) = Indicators.MACDSeries(closes);

			if (rsi.Count <= idx || hist.Count <= idx)
				return Hold("Indicators not ready");

			// Look for divergence over last 15 bars
			int lookback = Math.Min(15, closes.Count - 1);

			// Find price swing points
			decimal priceHigh = closes.Skip(idx - lookback).Take(lookback).Max();
			decimal priceLow = closes.Skip(idx - lookback).Take(lookback).Min();
			int priceHighIdx = idx - lookback + closes.Skip(idx - lookback).Take(lookback).ToList().IndexOf(priceHigh);
			int priceLowIdx = idx - lookback + closes.Skip(idx - lookback).Take(lookback).ToList().IndexOf(priceLow);

			// Volume analysis
			var volAnalysis = IndicatorsEnhanced.AnalyzeVolume(closes, volumes, 20);

			decimal rsiNow = rsi[idx];
			decimal histNow = hist[idx];

			// Bullish divergence: Price makes lower low, RSI/MACD makes higher low
			if (priceLowIdx < idx - 3)
			{
				decimal priceLowAtIdx = closes[priceLowIdx];
				decimal rsiLowAtIdx = rsi.Count > priceLowIdx ? rsi[priceLowIdx] : 50m;
				decimal histLowAtIdx = hist.Count > priceLowIdx ? hist[priceLowIdx] : 0m;

				bool priceLowerLow = closes[idx] < priceLowAtIdx;
				bool rsiHigherLow = rsiNow > rsiLowAtIdx;
				bool macdHigherLow = histNow > histLowAtIdx;

				// Bullish divergence detected
				if (priceLowerLow && (rsiHigherLow || macdHigherLow) &&
					rsiNow < 40m && histNow > hist[idx - 1])
				{
					decimal strength = Clamp01(
						0.5m +
						(rsiHigherLow ? 0.2m : 0m) +
						(macdHigherLow ? 0.2m : 0m) +
						(volAnalysis.IsAccumulation ? 0.1m : 0m)
					);

					string divergenceType = rsiHigherLow && macdHigherLow ? "RSI+MACD" :
											rsiHigherLow ? "RSI" : "MACD";

					return new("Buy", strength,
						$"Bullish {divergenceType} divergence (RSI={rsiNow:F1}, MACD hist={histNow:F4})");
				}
			}

			// Bearish divergence: Price makes higher high, RSI/MACD makes lower high
			if (priceHighIdx < idx - 3)
			{
				decimal priceHighAtIdx = closes[priceHighIdx];
				decimal rsiHighAtIdx = rsi.Count > priceHighIdx ? rsi[priceHighIdx] : 50m;
				decimal histHighAtIdx = hist.Count > priceHighIdx ? hist[priceHighIdx] : 0m;

				bool priceHigherHigh = closes[idx] > priceHighAtIdx;
				bool rsiLowerHigh = rsiNow < rsiHighAtIdx;
				bool macdLowerHigh = histNow < histHighAtIdx;

				// Bearish divergence detected
				if (priceHigherHigh && (rsiLowerHigh || macdLowerHigh) &&
					rsiNow > 60m && histNow < hist[idx - 1])
				{
					decimal strength = Clamp01(
						0.5m +
						(rsiLowerHigh ? 0.2m : 0m) +
						(macdLowerHigh ? 0.2m : 0m) +
						(volAnalysis.IsDistribution ? 0.1m : 0m)
					);

					string divergenceType = rsiLowerHigh && macdLowerHigh ? "RSI+MACD" :
											rsiLowerHigh ? "RSI" : "MACD";

					return new("Sell", strength,
						$"Bearish {divergenceType} divergence (RSI={rsiNow:F1}, MACD hist={histNow:F4})");
				}
			}

			return Hold($"No divergence detected (RSI={rsiNow:F1})");
		}

		// ═══════════════════════════════════════════════════════════════
		// 5) COMPOSITE QUALITY SCORE
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Calculates a comprehensive trade quality score
		/// Use this to filter and rank trade signals
		/// </summary>
		public static decimal CalculateTradeQualityScore(
			List<decimal> opens,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			string proposedDirection)
		{
			if (closes.Count < 100) return 0m;

			decimal score = 0m;
			int idx = closes.Count - 1;

			// 1. Market Regime Score (25%)
			var regime = IndicatorsEnhanced.DetectMarketRegime(closes, highs, lows);
			bool correctRegime = (proposedDirection == "Buy" && regime.IsTrendingUp) ||
								(proposedDirection == "Sell" && regime.IsTrendingDown);
			score += correctRegime ? 25m * regime.RegimeConfidence : 0m;

			// 2. Multi-Timeframe Alignment (20%)
			var mtf = IndicatorsEnhanced.AnalyzeMultiTimeframe(closes, highs, lows);
			bool mtfAligned = (proposedDirection == "Buy" && mtf.CurrentTFTrend == "Up" && mtf.IsAligned) ||
							 (proposedDirection == "Sell" && mtf.CurrentTFTrend == "Down" && mtf.IsAligned);
			score += mtfAligned ? 20m * mtf.Confidence : 0m;

			// 3. Volume Confirmation (15%)
			if (volumes != null && volumes.Count == closes.Count)
			{
				var volAnalysis = IndicatorsEnhanced.AnalyzeVolume(closes, volumes, 20);
				bool volConfirm = (proposedDirection == "Buy" && volAnalysis.IsAccumulation) ||
								 (proposedDirection == "Sell" && volAnalysis.IsDistribution);
				score += volConfirm ? 15m * volAnalysis.VolumeStrength : 0m;
			}

			// 4. Support/Resistance Proximity (15%)
			var srLevels = IndicatorsEnhanced.FindSupportResistance(highs, lows, closes);
			decimal price = closes[idx];

			if (proposedDirection == "Buy")
			{
				var nearSupport = srLevels.Where(l => l.IsSupport && l.Level < price)
										  .OrderByDescending(l => l.Level)
										  .FirstOrDefault();
				if (nearSupport != null)
				{
					decimal distance = Math.Abs(price - nearSupport.Level) / price;
					score += distance < 0.02m ? 15m * nearSupport.Strength : 0m;
				}
			}
			else if (proposedDirection == "Sell")
			{
				var nearResistance = srLevels.Where(l => !l.IsSupport && l.Level > price)
											 .OrderBy(l => l.Level)
											 .FirstOrDefault();
				if (nearResistance != null)
				{
					decimal distance = Math.Abs(nearResistance.Level - price) / price;
					score += distance < 0.02m ? 15m * nearResistance.Strength : 0m;
				}
			}

			// 5. Candlestick Pattern (10%)
			var patterns = IndicatorsEnhanced.RecognizePatterns(opens, highs, lows, closes);
			var matchingPattern = patterns.FirstOrDefault(p =>
				(proposedDirection == "Buy" && p.Signal == "Bullish") ||
				(proposedDirection == "Sell" && p.Signal == "Bearish"));

			if (matchingPattern != null)
			{
				score += 10m * matchingPattern.Strength;
			}

			// 6. Momentum Indicators (15%)
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count > idx)
			{
				decimal rsiValue = rsi[idx];
				bool rsiGood = (proposedDirection == "Buy" && rsiValue > 35m && rsiValue < 70m) ||
							  (proposedDirection == "Sell" && rsiValue < 65m && rsiValue > 30m);
				score += rsiGood ? 15m : 0m;
			}

			return Math.Min(score / 100m, 1m); // Normalize to 0-1
		}
	}
}