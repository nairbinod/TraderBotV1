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

			// STRICTER: Only trade in STRONG trending markets
			if (regime.Regime != IndicatorsEnhanced.MarketRegime.StrongTrending)
			{
				return Hold($"Not strong trending - {regime.Description}");
			}

			// STRICTER: Require high regime confidence
			if (regime.RegimeConfidence < 0.7m)
			{
				return Hold($"Low regime confidence: {regime.RegimeConfidence:P0}");
			}

			// Multi-timeframe analysis
			var mtf = IndicatorsEnhanced.AnalyzeMultiTimeframe(closes, highs, lows);

			// STRICTER: Must be aligned
			if (!mtf.IsAligned)
			{
				return Hold($"Timeframes not aligned: {mtf.Reason}");
			}

			// STRICTER: Require high MTF confidence
			if (mtf.Confidence < 0.7m)
			{
				return Hold($"Low MTF confidence: {mtf.Confidence:P0}");
			}

			// Calculate moving averages
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = Indicators.EMAList(closes, 200);

			int idx = closes.Count - 1;
			decimal price = closes[idx];

			// RSI confirmation
			var rsi = Indicators.RSIList(closes, 14);
			decimal rsiValue = rsi.Count > idx ? rsi[idx] : 50m;

			// STRICTER: Check price momentum (must be accelerating)
			bool strongMomentum = closes[idx] > closes[idx - 3] &&
								 closes[idx - 3] > closes[idx - 6];

			if (mtf.CurrentTFTrend == "Up")
			{
				// STRICTER: Must be clearly above EMA200
				if (price < ema200[idx] * 1.02m)
				{
					return Hold($"Price not clearly above EMA200 ({price:F2} vs {ema200[idx]:F2})");
				}

				// STRICTER: Must have pullback to moving average (no chasing)
				bool atPullback = (price >= ema20[idx] * 0.98m && price <= ema20[idx] * 1.005m) ||
								 (price >= ema50[idx] * 0.97m && price <= ema50[idx] * 1.01m);

				if (!atPullback)
				{
					return Hold($"No pullback to EMA - avoid chasing (price {price:F2} vs EMA20 {ema20[idx]:F2})");
				}

				// STRICTER: RSI must be in sweet spot (not overbought, not oversold)
				if (rsiValue < 45m || rsiValue > 65m)
				{
					return Hold($"RSI outside optimal range: {rsiValue:F1} (need 45-65)");
				}

				// NEW: Check recent price action (must not be extended)
				var recentPrices = closes.Skip(Math.Max(0, idx - 10)).Take(10).ToList();
				decimal highestRecent = recentPrices.Max();
				decimal extensionFromHigh = (highestRecent - price) / highestRecent;

				if (extensionFromHigh < 0.01m) // Within 1% of recent high
				{
					return Hold($"Too extended - at recent high (only {extensionFromHigh:P1} pullback)");
				}

				// NEW: Volume check (must have some interest)
				// This would need volumes parameter - skip for now

				// STRICTER: Require strong momentum
				if (!strongMomentum)
				{
					return Hold("Weak momentum - need 3-bar acceleration");
				}

				// STRICTER: Lower confidence score
				decimal strength = Clamp01(
					mtf.Confidence * 0.3m +
					regime.RegimeConfidence * 0.3m +
					0.2m  // Base reduced from 0.4
				);

				return new("Buy", strength,
					$"MTF uptrend pullback entry (RSI={rsiValue:F1}, confidence={strength:P0})");
			}
			else if (mtf.CurrentTFTrend == "Down")
			{
				// STRICTER: Same logic for sells
				if (price > ema200[idx] * 0.98m)
				{
					return Hold($"Price not clearly below EMA200");
				}

				bool atPullback = (price <= ema20[idx] * 1.02m && price >= ema20[idx] * 0.995m) ||
								 (price <= ema50[idx] * 1.03m && price >= ema50[idx] * 0.99m);

				if (!atPullback)
				{
					return Hold($"No pullback to EMA - avoid chasing");
				}

				if (rsiValue > 55m || rsiValue < 35m)
				{
					return Hold($"RSI outside optimal range: {rsiValue:F1} (need 35-55)");
				}

				var recentPrices = closes.Skip(Math.Max(0, idx - 10)).Take(10).ToList();
				decimal lowestRecent = recentPrices.Min();
				decimal extensionFromLow = (price - lowestRecent) / lowestRecent;

				if (extensionFromLow < 0.01m)
				{
					return Hold($"Too extended - at recent low");
				}

				if (!strongMomentum)
				{
					return Hold("Weak momentum");
				}

				decimal strength = Clamp01(
					mtf.Confidence * 0.3m +
					regime.RegimeConfidence * 0.3m +
					0.2m
				);

				return new("Sell", strength,
					$"MTF downtrend pullback entry (RSI={rsiValue:F1})");
			}

			return Hold($"No high-quality MTF setup");
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