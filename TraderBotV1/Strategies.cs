using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public record StrategySignal(string Signal, decimal Strength, string Reason);

	/// <summary>
	/// Unified Strategies - All 22 trading strategies combined
	/// </summary>
	public static class Strategies
	{
		private static StrategySignal Hold(string reason = "no setup") => new("Hold", 0m, reason);
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));

		// ═══ CORE STRATEGIES ═══

		public static StrategySignal EmaRsi(
					List<decimal> closes,
					int fast = 9,
					int slow = 21,
					int rsiPeriod = 14)
		{
			if (closes.Count < slow + 10) return Hold("insufficient data");

			var emaFast = Indicators.EMAList(closes, fast);
			var emaSlow = Indicators.EMAList(closes, slow);
			var rsiList = Indicators.RSIList(closes, rsiPeriod);

			if (emaFast.Count < 5 || emaSlow.Count < 5 || rsiList.Count < 5)
				return Hold("indicators not ready");

			int idx = closes.Count - 1;
			var context = SignalValidator.AnalyzeMarketContext(closes, closes, closes, idx);

			// Check for crossover
			bool crossUp = emaFast[idx] > emaSlow[idx] && emaFast[idx - 1] <= emaSlow[idx - 1];
			bool crossDown = emaFast[idx] < emaSlow[idx] && emaFast[idx - 1] >= emaSlow[idx - 1];

			if (crossUp)
			{
				var validation = SignalValidator.ValidateEMACrossover(closes, emaFast, emaSlow, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"EMA buy rejected: {validation.Reason}");

				// Additional RSI confirmation
				var rsiValidation = SignalValidator.ValidateRSI(rsiList, closes, idx, "Buy");
				bool rsiConfirm = rsiValidation.IsValid;

				decimal finalConfidence = rsiConfirm
					? (validation.Confidence * 0.6m + rsiValidation.Confidence * 0.4m)
					: validation.Confidence * 0.75m;

				return new("Buy", finalConfidence,
					$"EMA crossover ↑ validated ({validation.Confidence:P0}) {(rsiConfirm ? "+ RSI" : "")}");
			}

			if (crossDown)
			{
				var validation = SignalValidator.ValidateEMACrossover(closes, emaFast, emaSlow, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"EMA sell rejected: {validation.Reason}");

				var rsiValidation = SignalValidator.ValidateRSI(rsiList, closes, idx, "Sell");
				bool rsiConfirm = rsiValidation.IsValid;

				decimal finalConfidence = rsiConfirm
					? (validation.Confidence * 0.6m + rsiValidation.Confidence * 0.4m)
					: validation.Confidence * 0.75m;

				return new("Sell", finalConfidence,
					$"EMA crossover ↓ validated ({validation.Confidence:P0}) {(rsiConfirm ? "+ RSI" : "")}");
			}

			return Hold($"No EMA crossover");
		}

		public static StrategySignal BollingerMeanReversion(
					List<decimal> closes,
					List<decimal?> upper,
					List<decimal?> lower,
					List<decimal?> middle,
					int rsiPeriod = 14)
		{
			if (closes.Count < 30 || upper.Count == 0 || lower.Count == 0)
				return Hold("insufficient data");

			int idx = closes.Count - 1;
			var price = closes[idx];
			var ub = upper[idx];
			var lb = lower[idx];
			var mid = middle[idx];

			if (ub is null || lb is null || mid is null)
				return Hold("bands not ready");

			var rsiList = Indicators.RSIList(closes, rsiPeriod);
			if (rsiList.Count == 0) return Hold("rsi not ready");
			var rsi = rsiList[^1];

			var context = SignalValidator.AnalyzeMarketContext(closes, closes, closes, idx);

			// Trend context - avoid mean reversion in strong trends
			bool strongUptrend = context.IsUptrend && context.TrendStrength > 0.015m;
			bool strongDowntrend = context.IsDowntrend && context.TrendStrength > 0.015m;

			// BUY near lower band (oversold)
			if (price <= lb.Value && rsi < 35m)
			{
				if (strongDowntrend)
					return Hold("Strong downtrend - avoid catching falling knife");

				// Validate RSI oversold
				var rsiValidation = SignalValidator.ValidateRSI(rsiList, closes, idx, "Buy");

				decimal depth = (lb.Value - price) / Math.Max(ub.Value - lb.Value, 1e-8m);
				decimal strength = Clamp01(depth * 0.5m + (rsiValidation.IsValid ? 0.3m : 0.1m));

				string reason = rsiValidation.IsValid
					? $"Bollinger buy validated (RSI={rsi:F1}, depth={depth:P1})"
					: $"Bollinger buy (weak confirmation, RSI={rsi:F1})";

				return new("Buy", strength, reason);
			}

			// SELL near upper band (overbought)
			if (price >= ub.Value && rsi > 65m)
			{
				if (strongUptrend)
					return Hold("Strong uptrend - avoid selling strength");

				var rsiValidation = SignalValidator.ValidateRSI(rsiList, closes, idx, "Sell");

				decimal depth = (price - ub.Value) / Math.Max(ub.Value - lb.Value, 1e-8m);
				decimal strength = Clamp01(depth * 0.5m + (rsiValidation.IsValid ? 0.3m : 0.1m));

				string reason = rsiValidation.IsValid
					? $"Bollinger sell validated (RSI={rsi:F1}, depth={depth:P1})"
					: $"Bollinger sell (weak confirmation, RSI={rsi:F1})";

				return new("Sell", strength, reason);
			}

			return Hold($"Price in middle band (RSI={rsi:F1})");
		}

		public static StrategySignal AtrBreakout(
							List<decimal> closes,
							List<decimal> highs,
							List<decimal> lows,
							List<decimal> atr,
							int emaFast = 9,
							int emaSlow = 21,
							int rsiPeriod = 14,
							decimal k = 1.4m)  // ⚖️ BALANCED: 1.4x (was 1.5x strict)
		{
			if (closes.Count < emaSlow + 5 || atr.Count < emaSlow + 5)
				return Hold("insufficient data");

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal prevHigh = highs[idx - 1];
			decimal prevLow = lows[idx - 1];
			decimal currAtr = atr[idx];

			if (currAtr <= 0) return Hold("invalid ATR");

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// STRICTER: Volatility must be significant but not extreme
			decimal volRatio = currAtr / Math.Max(price, 1e-8m);
			if (volRatio < 0.004m)  // ⚖️ BALANCED: 0.4% (was 0.8%)
				return Hold($"Insufficient volatility: {volRatio:P2} (need >0.4%)");

			if (context.VolatilityRatio > 3.5m)  // ⚖️ BALANCED: Allow higher volatility (was 2.5x)
				return Hold($"Volatility too extreme: {context.VolatilityRatio:F2}x");

			var emaFastList = Indicators.EMAList(closes, emaFast);
			var emaSlowList = Indicators.EMAList(closes, emaSlow);
			var rsiList = Indicators.RSIList(closes, rsiPeriod);

			bool trendUp = emaFastList[idx] > emaSlowList[idx];
			bool trendDown = emaFastList[idx] < emaSlowList[idx];

			// STRICTER: Require CLEAR trend separation
			decimal emaSeparation = Math.Abs(emaFastList[idx] - emaSlowList[idx]) / emaSlowList[idx];
			if (emaSeparation < 0.003m)  // ⚖️ BALANCED: 0.3% (was 1%)
			{
				return Hold($"EMA separation too small: {emaSeparation:P2}");
			}

			decimal rsi = rsiList.Count > idx ? rsiList[idx] : 50m;

			decimal buyThresh = prevHigh + k * currAtr;
			decimal sellThresh = prevLow - k * currAtr;

			// NEW: Check if we've been consolidating (ADX check would be better)
			var recentRange = highs.Skip(Math.Max(0, idx - 20)).Take(20).Max() -
							 lows.Skip(Math.Max(0, idx - 20)).Take(20).Min();
			var avgPrice = closes.Skip(Math.Max(0, idx - 20)).Take(20).Average();
			decimal consolidationTightness = recentRange / avgPrice;

			if (consolidationTightness > 0.25m)  // ⚖️ BALANCED: 25% (was 15%)
			{
				return Hold($"Not consolidating enough for breakout (range: {consolidationTightness:P1})");
			}

			// BUY breakout - MUCH STRICTER
			if (price > buyThresh && trendUp)
			{
				// STRICTER: Must be clear breakout (not marginal)
				decimal breakoutMargin = (price - buyThresh) / currAtr;
				if (breakoutMargin < 0.1m)  // ⚖️ BALANCED: 10% of ATR (was 30%)
				{
					return Hold($"Breakout margin too small: {breakoutMargin:F2} ATRs");
				}

				// STRICTER: Previous bar must be near or below threshold
				if (closes[idx - 1] > buyThresh * 0.99m)  // ⚖️ BALANCED: Less strict
					return Hold("No clear breakout (already above threshold)");

				// STRICTER: RSI range (not overbought)
				if (rsi > 75m)  // ⚖️ BALANCED: 75 (was 70)
					return Hold($"RSI overbought: {rsi:F1}");

				// STRICTER: Must have strong momentum over multiple bars
				int consecutiveUp = 0;
				for (int i = idx; i > idx - 4 && i > 0; i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveUp++;
				}

				if (consecutiveUp < 2)
				{
					return Hold("Insufficient momentum (need 2+ consecutive up bars)");
				}

				// STRICTER: Lower confidence
				decimal breakoutStrength = (price - buyThresh) / currAtr;
				decimal strength = Clamp01(breakoutStrength * 0.3m + 0.55m);  // ⚖️ BALANCED: Higher confidence

				return new("Buy", strength,
					$"ATR breakout (${price:F2}>${buyThresh:F2}, RSI={rsi:F1}, margin={breakoutMargin:F2})");
			}

			// SELL breakdown - MUCH STRICTER
			if (price < sellThresh && trendDown)
			{
				decimal breakdownMargin = (sellThresh - price) / currAtr;
				if (breakdownMargin < 0.1m)  // ⚖️ BALANCED
				{
					return Hold($"Breakdown margin too small: {breakdownMargin:F2} ATRs");
				}

				if (closes[idx - 1] < sellThresh * 1.01m)  // ⚖️ BALANCED
					return Hold("No clear breakdown");

				if (rsi < 25m)  // ⚖️ BALANCED: 25 (was 30)
					return Hold($"RSI oversold: {rsi:F1}");

				int consecutiveDown = 0;
				for (int i = idx; i > idx - 4 && i > 0; i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveDown++;
				}

				if (consecutiveDown < 2)
				{
					return Hold("Insufficient momentum");
				}

				decimal breakdownStrength = (sellThresh - price) / currAtr;
				decimal strength = Clamp01(breakdownStrength * 0.2m + 0.4m);

				return new("Sell", strength,
					$"ATR breakdown (${price:F2}<${sellThresh:F2}, RSI={rsi:F1})");
			}

			return Hold($"No ATR breakout (price ${price:F2}, buy>${buyThresh:F2}, sell<${sellThresh:F2})");
		}

		public static StrategySignal MacdDivergence(
					List<decimal> closes,
					List<decimal> macd,
					List<decimal> signal,
					List<decimal> hist)
		{
			if (closes.Count < 60 || macd.Count < 60 || hist.Count < 60)
				return Hold("Insufficient data for MACD");

			int idx = closes.Count - 1;
			var context = SignalValidator.AnalyzeMarketContext(closes, closes, closes, idx);

			// Skip in choppy markets
			if (context.IsSideways || context.RecentRange < 0.008m)
				return Hold($"Choppy market - MACD unreliable (range={context.RecentRange:P2})");

			// Check for crossover
			bool crossUp = hist[idx] > 0 && hist[idx - 1] <= 0;
			bool crossDown = hist[idx] < 0 && hist[idx - 1] >= 0;

			if (crossUp)
			{
				var validation = SignalValidator.ValidateMACD(hist, closes, macd, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"MACD buy rejected: {validation.Reason}");

				// Check for bullish divergence (bonus)
				bool bullishDiv = closes[idx] < closes[idx - 5] && macd[idx] > macd[idx - 5];

				decimal confidence = validation.Confidence;
				if (bullishDiv) confidence = Math.Min(confidence + 0.15m, 1m);

				return new("Buy", confidence,
					$"MACD buy validated{(bullishDiv ? " + divergence" : "")} (hist={hist[idx]:F4})");
			}

			if (crossDown)
			{
				var validation = SignalValidator.ValidateMACD(hist, closes, macd, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"MACD sell rejected: {validation.Reason}");

				bool bearishDiv = closes[idx] > closes[idx - 5] && macd[idx] < macd[idx - 5];

				decimal confidence = validation.Confidence;
				if (bearishDiv) confidence = Math.Min(confidence + 0.15m, 1m);

				return new("Sell", confidence,
					$"MACD sell validated{(bearishDiv ? " + divergence" : "")} (hist={hist[idx]:F4})");
			}

			return Hold($"No MACD crossover (hist={hist[idx]:F4})");
		}

		// ═══ EXTENDED STRATEGIES ═══

		public static StrategySignal AdxFilter(
							List<decimal> highs,
							List<decimal> lows,
							List<decimal> closes,
							int period = 14,
							decimal threshold = 25m)  // ⚖️ BALANCED: 25 (was 30)
		{
			var (adx, diPlus, diMinus) = Indicators.ADXList(highs, lows, closes, period);
			if (adx.Count < 5) return Hold("ADX insufficient data");

			int idx = adx.Count - 1;
			decimal adxNow = adx[idx];
			decimal adxPrev = adx[idx - 1];

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// STRICTER: Skip if volatility too low
			if (context.RecentRange < 0.006m)  // ⚖️ BALANCED: 0.6% (was 1%)
				return Hold($"Low volatility: range={context.RecentRange:P2}");

			// STRICTER: ADX must be STRONG
			if (adxNow < threshold)
			{
				return Hold($"ADX too weak: {adxNow:F1} < {threshold}");
			}

			// NEW: ADX must be rising (strengthening trend)
			if (adxNow <= adxPrev)
			{
				return Hold($"ADX not rising ({adxNow:F1} vs {adxPrev:F1})");
			}

			// STRICTER: DI+ and DI- separation must be CLEAR
			decimal diSeparation = Math.Abs(diPlus[idx] - diMinus[idx]);
			if (diSeparation < 5m)  // ⚖️ BALANCED: 5 (was 10)
			{
				return Hold($"DI separation too small: {diSeparation:F1} (need >5)");
			}

			// Check for uptrend - STRICTER
			if (diPlus[idx] > diMinus[idx])
			{
				// STRICTER: DI+ must be clearly dominant
				if (diPlus[idx] < diMinus[idx] + 10m)
					return Hold($"DI+ not dominant enough: {diPlus[idx]:F1} vs {diMinus[idx]:F1}");

				// NEW: DI+ must be rising
				if (idx > 0 && diPlus[idx] <= diPlus[idx - 1])
					return Hold("DI+ not rising");

				// STRICTER: Lower confidence
				decimal strength = Clamp01((adxNow - threshold) / 20m + 0.55m);  // ⚖️ BALANCED: Higher base

				return new("Buy", strength,
					$"Strong uptrend (ADX={adxNow:F1}, DI+={diPlus[idx]:F1})");
			}

			// Check for downtrend - STRICTER
			if (diMinus[idx] > diPlus[idx])
			{
				if (diMinus[idx] < diPlus[idx] + 10m)
					return Hold($"DI- not dominant enough: {diMinus[idx]:F1} vs {diPlus[idx]:F1}");

				if (idx > 0 && diMinus[idx] <= diMinus[idx - 1])
					return Hold("DI- not rising");

				decimal strength = Clamp01((adxNow - threshold) / 20m + 0.55m);  // ⚖️ BALANCED

				return new("Sell", strength,
					$"Strong downtrend (ADX={adxNow:F1}, DI-={diMinus[idx]:F1})");
			}

			return Hold($"ADX unclear (DI+={diPlus[idx]:F1}, DI-={diMinus[idx]:F1})");
		}

		public static StrategySignal VolumeConfirm(
				List<decimal> closes,
				List<decimal> volumes,
				int period = 20,
				decimal spikeMultiple = 1.3m)  // ⚖️ BALANCED: 1.3x (was 2.0x)
		{
			if (volumes == null || volumes.Count < closes.Count || volumes.Count < period + 5)
				return Hold("Volume data insufficient");

			int idx = closes.Count - 1;

			// STRICTER: Volume must be SIGNIFICANTLY higher
			decimal currentVol = volumes[idx];
			var recentVols = volumes.Skip(Math.Max(0, idx - period)).Take(period).ToList();
			decimal avgVol = recentVols.Average();

			if (avgVol <= 0)
				return Hold("Invalid volume baseline");

			decimal volRatio = currentVol / avgVol;

			if (volRatio < spikeMultiple)
				return Hold($"Volume spike not strong enough: {volRatio:F2}x (need >{spikeMultiple:F1}x)");

			// NEW: Price movement must be significant (not just a small tick)
			decimal priceChange = Math.Abs(closes[idx] - closes[idx - 1]) / closes[idx - 1];
			if (priceChange < 0.005m)  // ⚖️ BALANCED: 0.5% move (was 1.5%)
				return Hold($"Price move too small: {priceChange:P2} (need >0.5%)");

			bool upBar = closes[idx] > closes[idx - 1];

			// NEW: Require TREND CONFIRMATION (not just random volume spike)
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			if (ema20.Count <= idx || ema50.Count <= idx)
				return Hold("Insufficient data for trend confirmation");

			bool uptrend = ema20[idx] > ema50[idx];
			bool downtrend = ema20[idx] < ema50[idx];

			// CRITICAL: Volume spike must align with established trend
			if (upBar && !uptrend)
				return Hold("Volume spike on up bar but no uptrend - avoid false signal");

			if (!upBar && !downtrend)
				return Hold("Volume spike on down bar but no downtrend - avoid false signal");

			// NEW: Require MOMENTUM (multiple consecutive bars in same direction)
			int consecutiveBars = 0;
			if (upBar)
			{
				for (int i = idx; i > idx - 4 && i > 0; i--)
				{
					if (closes[i] > closes[i - 1])
						consecutiveBars++;
				}
				if (consecutiveBars < 2)
					return Hold("Insufficient upward momentum (need 2+ consecutive up bars)");
			}
			else
			{
				for (int i = idx; i > idx - 4 && i > 0; i--)
				{
					if (closes[i] < closes[i - 1])
						consecutiveBars++;
				}
				if (consecutiveBars < 2)
					return Hold("Insufficient downward momentum (need 2+ consecutive down bars)");
			}

			// NEW: RSI CONFIRMATION (avoid extreme overbought/oversold)
			var rsi = Indicators.RSIList(closes, 14);
			if (rsi.Count <= idx)
				return Hold("RSI data not available");

			decimal rsiValue = rsi[idx];

			if (upBar)
			{
				if (rsiValue > 80m)  // ⚖️ BALANCED: 80 (was 75)
					return Hold($"RSI too high: {rsiValue:F1} - avoid buying into overbought");

				if (rsiValue < 30m)  // ⚖️ BALANCED: 30 (was 40)
					return Hold($"RSI too low: {rsiValue:F1} - need confirmation of strength");
			}
			else
			{
				if (rsiValue < 20m)  // ⚖️ BALANCED: 20 (was 25)
					return Hold($"RSI too low: {rsiValue:F1} - avoid selling into oversold");

				if (rsiValue > 70m)  // ⚖️ BALANCED: 70 (was 60)
					return Hold($"RSI too high: {rsiValue:F1} - need confirmation of weakness");
			}

			// NEW: Check EMA separation (strong trend confirmation)
			decimal emaSeparation = Math.Abs(ema20[idx] - ema50[idx]) / ema50[idx];
			if (emaSeparation < 0.01m)  // ⚖️ BALANCED: 1% (was 2%)
				return Hold($"Trend not strong enough: EMA separation {emaSeparation:P2} (need >1%)");

			// NEW: Lower confidence due to volume signals being less reliable
			decimal baseConfidence = 0.6m;  // ⚖️ BALANCED: Start at 60% (was 50%)

			// Add confidence for exceptional volume
			if (volRatio > 3.0m)
				baseConfidence += 0.15m;  // Very high volume

			// Add confidence for strong price move
			if (priceChange > 0.03m)
				baseConfidence += 0.1m;  // >3% move

			// Add confidence for strong momentum
			if (consecutiveBars >= 3)
				baseConfidence += 0.1m;

			decimal finalConfidence = Clamp01(baseConfidence);

			string direction = upBar ? "Buy" : "Sell";
			string reason = $"High volume {direction.ToLower()} confirmed: {volRatio:F1}x vol, {priceChange:P1} move, {consecutiveBars} bars momentum, RSI={rsiValue:F1}";

			return new(direction, finalConfidence, reason);
		}

		public static StrategySignal CciReversion(
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> closes,
					int period = 20)
		{
			var cci = Indicators.CCIList(highs, lows, closes, period);
			if (cci.Count < 5) return Hold("CCI insufficient data");

			int idx = cci.Count - 1;
			decimal cciNow = cci[idx];
			decimal cciPrev = cci[idx - 1];

			// Check for buy signal
			if (cciPrev <= -100m && cciNow > -100m)
			{
				var validation = SignalValidator.ValidateCCI(cci, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"CCI buy rejected: {validation.Reason}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for sell signal
			if (cciPrev >= 100m && cciNow < 100m)
			{
				var validation = SignalValidator.ValidateCCI(cci, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"CCI sell rejected: {validation.Reason}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"CCI neutral ({cciNow:F0})");
		}

		public static StrategySignal DonchianBreakout(
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> closes,
					int period = 20)
		{
			if (closes.Count < period + 10)
				return Hold("Donchian insufficient data");

			var (upper, lower) = Indicators.DonchianChannel(highs, lows, period);
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			var rsi = Indicators.RSIList(closes, 14);

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal u = upper[idx];
			decimal l = lower[idx];

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// Check for breakout above upper band
			if (price > u)
			{
				var validation = SignalValidator.ValidateDonchianBreakout(
					closes, highs, lows, upper, lower, atr, idx, "Buy");

				if (!validation.IsValid)
					return Hold($"Donchian buy rejected: {validation.Reason}");

				// Additional RSI check
				decimal rsiNow = rsi.Count > idx ? rsi[idx] : 50m;
				if (rsiNow > 75m)
					return Hold($"RSI too high: {rsiNow:F1}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for breakdown below lower band
			if (price < l)
			{
				var validation = SignalValidator.ValidateDonchianBreakout(
					closes, highs, lows, upper, lower, atr, idx, "Sell");

				if (!validation.IsValid)
					return Hold($"Donchian sell rejected: {validation.Reason}");

				decimal rsiNow = rsi.Count > idx ? rsi[idx] : 50m;
				if (rsiNow < 25m)
					return Hold($"RSI too low: {rsiNow:F1}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"Price within channel (${price:F2}, U=${u:F2}, L=${l:F2})");
		}

		public static StrategySignal PivotReversal(
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> closes)
		{
			if (closes.Count < 5) return Hold("Not enough bars");

			int idx = closes.Count - 1;
			decimal prevHigh = highs[idx - 1];
			decimal prevLow = lows[idx - 1];
			decimal prevClose = closes[idx - 1];
			var (P, R1, S1) = Indicators.PivotPoints(prevHigh, prevLow, prevClose);

			decimal price = closes[idx];
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// Sell at resistance (only if not in strong uptrend)
			if (price >= R1 && price > prevClose)
			{
				if (context.IsUptrend && context.TrendStrength > 0.02m)
					return Hold("Strong uptrend - avoid selling at R1");

				decimal distance = (price - R1) / Math.Max(price * 0.01m, 1e-8m);
				decimal strength = Clamp01(distance + 0.5m);
				return new("Sell", strength, $"At/above R1 resistance (${R1:F2})");
			}

			// Buy at support (only if not in strong downtrend)
			if (price <= S1 && price < prevClose)
			{
				if (context.IsDowntrend && context.TrendStrength > 0.02m)
					return Hold("Strong downtrend - avoid buying at S1");

				decimal distance = (S1 - price) / Math.Max(price * 0.01m, 1e-8m);
				decimal strength = Clamp01(distance + 0.5m);
				return new("Buy", strength, $"At/below S1 support (${S1:F2})");
			}

			return Hold($"Away from pivots (P=${P:F2}, R1=${R1:F2}, S1=${S1:F2})");
		}

		public static StrategySignal StochRsiReversal(
					List<decimal> closes,
					int rsiPeriod = 14,
					int stochPeriod = 14,
					int smoothK = 3,
					int smoothD = 3)
		{
			var (k, d) = Indicators.StochRSIList(closes, rsiPeriod, stochPeriod, smoothK, smoothD);
			var rsi = Indicators.RSIList(closes, rsiPeriod);

			if (k.Count < 5 || d.Count < 5)
				return Hold("StochRSI insufficient data");

			int idx = k.Count - 1;
			decimal kNow = k[idx];
			decimal kPrev = k[idx - 1];
			decimal dNow = d[idx];
			decimal dPrev = d[idx - 1];

			// Avoid neutral zone
			if (kNow > 0.4m && kNow < 0.6m)
				return Hold($"StochRSI neutral (K={kNow:F2})");

			// Check for buy signal
			bool crossUp = kNow > dNow && kPrev <= dPrev;
			if (crossUp && kNow < 0.5m)
			{
				var validation = SignalValidator.ValidateStochRSI(k, d, rsi, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"StochRSI buy rejected: {validation.Reason}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for sell signal
			bool crossDown = kNow < dNow && kPrev >= dPrev;
			if (crossDown && kNow > 0.5m)
			{
				var validation = SignalValidator.ValidateStochRSI(k, d, rsi, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"StochRSI sell rejected: {validation.Reason}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"StochRSI no signal (K={kNow:F2}, D={dNow:F2})");
		}

		public static StrategySignal Ema200RegimeFilter(
					List<decimal> closes,
					int period = 200)
		{
			if (closes.Count < period + 10)
				return Hold("Insufficient data for EMA200");

			var ema = Indicators.EMAList(closes, period);
			if (ema.Count < 5) return Hold("EMA data insufficient");

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal emaVal = ema[idx];
			decimal emaPrev = ema[idx - 1];

			var context = SignalValidator.AnalyzeMarketContext(closes, closes, closes, idx);

			// Calculate distance from EMA
			decimal distance = Math.Abs(price - emaVal) / Math.Max(price, 1e-8m);

			// EMA slope (trend direction)
			bool emaRising = emaVal > emaPrev;
			bool emaFalling = emaVal < emaPrev;

			// Above EMA (bullish regime)
			if (price > emaVal)
			{
				// Stronger signal if EMA is also rising
				decimal confidence = emaRising
					? Clamp01(distance * 10m + 0.6m)
					: Clamp01(distance * 10m + 0.4m);

				string reason = emaRising
					? $"Above rising EMA{period} (${emaVal:F2})"
					: $"Above flat/falling EMA{period} (${emaVal:F2})";

				return new("Buy", confidence, reason);
			}

			// Below EMA (bearish regime)
			if (price < emaVal)
			{
				decimal confidence = emaFalling
					? Clamp01(distance * 10m + 0.6m)
					: Clamp01(distance * 10m + 0.4m);

				string reason = emaFalling
					? $"Below falling EMA{period} (${emaVal:F2})"
					: $"Below flat/rising EMA{period} (${emaVal:F2})";

				return new("Sell", confidence, reason);
			}

			return Hold($"At EMA{period} (${emaVal:F2})");
		}

		// ═══ ADVANCED STRATEGIES ═══

		public static StrategySignal VWAPStrategy(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> volumes)
		{
			if (closes.Count < 20 || volumes == null || volumes.Count != closes.Count)
				return Hold("Insufficient data for VWAP");

			int lookback = Math.Min(20, closes.Count);
			decimal vwap = CalculateVWAP(closes, highs, lows, volumes, lookback);

			if (vwap == 0) return Hold("Invalid VWAP");

			decimal price = closes.Last();
			decimal deviation = (price - vwap) / vwap;

			// Calculate volume trend
			var recentVol = volumes.Skip(volumes.Count - 5).Average();
			var avgVol = volumes.Skip(Math.Max(0, volumes.Count - 20)).Take(20).Average();
			decimal volRatio = avgVol > 0 ? recentVol / avgVol : 1m;

			// Buy when price crosses above VWAP with volume
			if (closes[closes.Count - 2] <= vwap && price > vwap && volRatio > 1.2m)
			{
				decimal strength = Clamp01(Math.Abs(deviation) * 10m + (volRatio - 1m) * 0.3m);
				return new("Buy", strength, $"Price crossed above VWAP (${vwap:F2}) with {volRatio:F1}x volume");
			}

			// Sell when price crosses below VWAP with volume
			if (closes[closes.Count - 2] >= vwap && price < vwap && volRatio > 1.2m)
			{
				decimal strength = Clamp01(Math.Abs(deviation) * 10m + (volRatio - 1m) * 0.3m);
				return new("Sell", strength, $"Price crossed below VWAP (${vwap:F2}) with {volRatio:F1}x volume");
			}

			return Hold($"Price near VWAP (${vwap:F2})");
		}

		public static StrategySignal IchimokuCloud(
							List<decimal> closes,
							List<decimal> highs,
							List<decimal> lows)
		{
			if (closes.Count < 52) return Hold("Insufficient data for Ichimoku");

			var (tenkan, kijun, senkouA, senkouB) = CalculateIchimoku(highs, lows, closes);

			decimal price = closes.Last();
			decimal cloudTop = Math.Max(senkouA, senkouB);
			decimal cloudBottom = Math.Min(senkouA, senkouB);
			decimal cloudThickness = cloudTop - cloudBottom;

			// NEW: Cloud must have minimum thickness (avoid thin clouds)
			if (cloudThickness / price < 0.01m)  // Less than 1% thick
			{
				return Hold($"Cloud too thin: {cloudThickness / price:P2} (need >1%)");
			}

			// Determine trend
			bool bullishCloud = senkouA > senkouB;
			bool priceAboveCloud = price > cloudTop;
			bool priceBelowCloud = price < cloudBottom;
			bool tenkanAboveKijun = tenkan > kijun;

			// STRICTER: Check TK separation
			decimal tkSeparation = Math.Abs(tenkan - kijun) / kijun;
			if (tkSeparation < 0.015m)  // Need 1.5%+ separation
			{
				return Hold($"TK lines too close: {tkSeparation:P2}");
			}

			// STRICTER: Price must be CLEARLY above/below cloud
			if (priceAboveCloud)
			{
				decimal distanceFromCloud = (price - cloudTop) / price;
				if (distanceFromCloud < 0.01m)  // Must be 1%+ above
				{
					return Hold($"Price too close to cloud top: {distanceFromCloud:P2}");
				}
			}
			else if (priceBelowCloud)
			{
				decimal distanceFromCloud = (cloudBottom - price) / price;
				if (distanceFromCloud < 0.01m)
				{
					return Hold($"Price too close to cloud bottom: {distanceFromCloud:P2}");
				}
			}

			// STRICTER: Strong buy - all conditions must be met
			if (priceAboveCloud && bullishCloud && tenkanAboveKijun)
			{
				// NEW: Check price momentum
				int idx = closes.Count - 1;
				bool strongMomentum = closes[idx] > closes[idx - 3];

				if (!strongMomentum)
				{
					return Hold("Price momentum weak despite bullish Ichimoku");
				}

				// STRICTER: Tenkan must be rising
				// (Would need previous tenkan value - approximate with price)
				if (price < tenkan * 1.005m)
				{
					return Hold("Price not above Tenkan");
				}

				// STRICTER: Lower confidence
				decimal strength = Clamp01((price - cloudTop) / price * 15m + 0.4m);  // REDUCED

				return new("Buy", strength,
					$"Bullish Ichimoku (price {(price - cloudTop) / price:P1} above cloud)");
			}

			// STRICTER: Strong sell - all conditions must be met
			if (priceBelowCloud && !bullishCloud && !tenkanAboveKijun)
			{
				int idx = closes.Count - 1;
				bool strongMomentum = closes[idx] < closes[idx - 3];

				if (!strongMomentum)
				{
					return Hold("Price momentum weak despite bearish Ichimoku");
				}

				if (price > tenkan * 0.995m)
				{
					return Hold("Price not below Tenkan");
				}

				decimal strength = Clamp01((cloudBottom - price) / price * 15m + 0.4m);

				return new("Sell", strength,
					$"Bearish Ichimoku (price {(cloudBottom - price) / price:P1} below cloud)");
			}

			// NO MORE BREAKOUT SIGNALS - Too prone to false signals
			return Hold("Ichimoku not in strong setup");
		}

		public static StrategySignal PriceActionTrend(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows)
		{
			if (closes.Count < 20) return Hold("Insufficient data");

			var swingPoints = FindSwingPoints(highs, lows, closes, lookback: 15);

			if (swingPoints.Count < 4) return Hold("Not enough swing points");

			// Check for higher highs and higher lows (uptrend)
			bool higherHighs = swingPoints.Where(p => p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value > a.value)
				.All(x => x);

			bool higherLows = swingPoints.Where(p => !p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => !p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value > a.value)
				.All(x => x);

			// Check for lower highs and lower lows (downtrend)
			bool lowerHighs = swingPoints.Where(p => p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value < a.value)
				.All(x => x);

			bool lowerLows = swingPoints.Where(p => !p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => !p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value < a.value)
				.All(x => x);

			// Confirmed uptrend
			if (higherHighs && higherLows)
			{
				decimal strength = 0.8m;
				return new("Buy", strength, "Strong uptrend: Higher highs & higher lows confirmed");
			}

			// Confirmed downtrend
			if (lowerHighs && lowerLows)
			{
				decimal strength = 0.8m;
				return new("Sell", strength, "Strong downtrend: Lower highs & lower lows confirmed");
			}

			return Hold("No clear price action trend");
		}

		public static StrategySignal SqueezeMomentum(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows,
					int bbPeriod = 20,
					int kcPeriod = 20,
					decimal bbMult = 2m,
					decimal kcMult = 1.5m)
		{
			if (closes.Count < Math.Max(bbPeriod, kcPeriod) + 10)
				return Hold("Insufficient data for Squeeze");

			var atr = Indicators.ATRList(highs, lows, closes, kcPeriod);
			var (bbUpper, bbMiddle, bbLower) = Indicators.BollingerBandsFast(closes, bbPeriod, bbMult);

			if (bbUpper.Count == 0 || atr.Count == 0) return Hold("Indicator calculation failed");

			int idx = closes.Count - 1;
			decimal close = closes[idx];
			decimal ema = bbMiddle[idx] ?? close;
			decimal atrValue = atr[idx];

			// Keltner Channels
			decimal kcUpper = ema + (kcMult * atrValue);
			decimal kcLower = ema - (kcMult * atrValue);

			// Squeeze: Bollinger Bands inside Keltner Channels
			bool squeeze = bbUpper[idx] < kcUpper && bbLower[idx] > kcLower;
			bool prevSqueeze = idx > 0 && bbUpper[idx - 1] < (ema + (kcMult * atr[idx - 1]));

			// Momentum indicator (simplified)
			decimal momentum = close - ema;
			decimal prevMomentum = idx > 0 ? closes[idx - 1] - (bbMiddle[idx - 1] ?? closes[idx - 1]) : 0;

			// Squeeze release with bullish momentum
			if (prevSqueeze && !squeeze && momentum > 0 && momentum > prevMomentum)
			{
				decimal strength = Clamp01(Math.Abs(momentum / close) * 20m + 0.6m);
				return new("Buy", strength, "Squeeze release: Bullish breakout with momentum");
			}

			// Squeeze release with bearish momentum
			if (prevSqueeze && !squeeze && momentum < 0 && momentum < prevMomentum)
			{
				decimal strength = Clamp01(Math.Abs(momentum / close) * 20m + 0.6m);
				return new("Sell", strength, "Squeeze release: Bearish breakdown with momentum");
			}

			if (squeeze)
				return Hold("In squeeze: Consolidation, waiting for breakout");

			return Hold("No squeeze setup");
		}

		public static StrategySignal MoneyFlowIndex(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> volumes,
					int period = 14)
		{
			if (closes.Count < period + 5 || volumes == null || volumes.Count != closes.Count)
				return Hold("Insufficient data for MFI");

			var mfi = CalculateMFI(highs, lows, closes, volumes, period);

			if (mfi.Count < 3) return Hold("MFI calculation incomplete");

			decimal mfiNow = mfi.Last();
			decimal mfiPrev = mfi[mfi.Count - 2];
			decimal mfiPrev2 = mfi[mfi.Count - 3];

			// Oversold reversal
			if (mfiPrev <= 20m && mfiNow > 20m && mfiNow > mfiPrev && mfiPrev > mfiPrev2)
			{
				decimal strength = Clamp01((mfiNow - 20m) / 30m + 0.6m);
				return new("Buy", strength, $"MFI oversold reversal (MFI={mfiNow:F1})");
			}

			// Overbought reversal
			if (mfiPrev >= 80m && mfiNow < 80m && mfiNow < mfiPrev && mfiPrev < mfiPrev2)
			{
				decimal strength = Clamp01((80m - mfiNow) / 30m + 0.6m);
				return new("Sell", strength, $"MFI overbought reversal (MFI={mfiNow:F1})");
			}

			// Divergence detection
			bool bullishDiv = closes.Last() < closes[closes.Count - 10] && mfiNow > mfi[mfi.Count - 10];
			bool bearishDiv = closes.Last() > closes[closes.Count - 10] && mfiNow < mfi[mfi.Count - 10];

			if (bullishDiv && mfiNow < 40m)
			{
				return new("Buy", 0.75m, $"Bullish MFI divergence (MFI={mfiNow:F1})");
			}

			if (bearishDiv && mfiNow > 60m)
			{
				return new("Sell", 0.75m, $"Bearish MFI divergence (MFI={mfiNow:F1})");
			}

			return Hold($"MFI neutral ({mfiNow:F1})");
		}

		public static StrategySignal ParabolicSAR(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows,
					decimal acceleration = 0.02m,
					decimal maximum = 0.2m)
		{
			if (closes.Count < 10) return Hold("Insufficient data for SAR");

			var sar = CalculateParabolicSAR(highs, lows, acceleration, maximum);

			if (sar.Count < 3) return Hold("SAR calculation incomplete");

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal sarNow = sar[idx];
			decimal sarPrev = sar[idx - 1];

			bool bullish = price > sarNow;
			bool wasBullish = closes[idx - 1] > sarPrev;

			// Bullish SAR flip
			if (bullish && !wasBullish)
			{
				decimal distance = Math.Abs(price - sarNow) / price;
				decimal strength = Clamp01(distance * 50m + 0.65m);
				return new("Buy", strength, $"SAR flip bullish (SAR=${sarNow:F2})");
			}

			// Bearish SAR flip
			if (!bullish && wasBullish)
			{
				decimal distance = Math.Abs(price - sarNow) / price;
				decimal strength = Clamp01(distance * 50m + 0.65m);
				return new("Sell", strength, $"SAR flip bearish (SAR=${sarNow:F2})");
			}

			return Hold($"SAR {(bullish ? "bullish" : "bearish")} (no flip)");
		}

		public static StrategySignal TripleEMA(
					List<decimal> closes,
					int fast = 8,
					int medium = 21,
					int slow = 50)
		{
			if (closes.Count < slow + 5) return Hold("Insufficient data for Triple EMA");

			var emaFast = Indicators.EMAList(closes, fast);
			var emaMedium = Indicators.EMAList(closes, medium);
			var emaSlow = Indicators.EMAList(closes, slow);

			int idx = closes.Count - 1;

			bool bullishAlignment = emaFast[idx] > emaMedium[idx] && emaMedium[idx] > emaSlow[idx];
			bool bearishAlignment = emaFast[idx] < emaMedium[idx] && emaMedium[idx] < emaSlow[idx];

			bool wasBullish = idx > 0 && emaFast[idx - 1] > emaMedium[idx - 1] &&
							 emaMedium[idx - 1] > emaSlow[idx - 1];
			bool wasBearish = idx > 0 && emaFast[idx - 1] < emaMedium[idx - 1] &&
							 emaMedium[idx - 1] < emaSlow[idx - 1];

			// Perfect bullish alignment with crossover
			if (bullishAlignment && !wasBullish)
			{
				decimal strength = 0.85m;
				return new("Buy", strength, "Triple EMA bullish alignment confirmed");
			}

			// Perfect bearish alignment with crossover
			if (bearishAlignment && !wasBearish)
			{
				decimal strength = 0.85m;
				return new("Sell", strength, "Triple EMA bearish alignment confirmed");
			}

			// Sustained trend
			if (bullishAlignment && wasBullish)
			{
				return new("Buy", 0.7m, "Triple EMA sustained uptrend");
			}

			if (bearishAlignment && wasBearish)
			{
				return new("Sell", 0.7m, "Triple EMA sustained downtrend");
			}

			return Hold("Triple EMA mixed or consolidating");
		}

		// ═══ ENHANCED STRATEGIES ═══

		public static StrategySignal TrendFollowingMTF(
							List<decimal> closes,
							List<decimal> highs,
							List<decimal> lows)
		{
			if (closes.Count < 200)
				return Hold("Insufficient data for MTF analysis");

			// Detect market regime
			var regime = Indicators.DetectMarketRegime(closes, highs, lows);

			// STRICTER: Only trade in STRONG trending markets
			if (regime.Regime != Indicators.MarketRegime.StrongTrending)
			{
				return Hold($"Not strong trending - {regime.Description}");
			}

			// STRICTER: Require high regime confidence
			if (regime.RegimeConfidence < 0.7m)
			{
				return Hold($"Low regime confidence: {regime.RegimeConfidence:P0}");
			}

			// Multi-timeframe analysis
			var mtf = Indicators.AnalyzeMultiTimeframe(closes, highs, lows);

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

		public static StrategySignal MeanReversionSR(
					List<decimal> closes,
					List<decimal> highs,
					List<decimal> lows,
					List<decimal> volumes)
		{
			if (closes.Count < 100)
				return Hold("Insufficient data");

			// Only trade mean reversion in ranging markets
			var regime = Indicators.DetectMarketRegime(closes, highs, lows);

			if (regime.Regime == Indicators.MarketRegime.StrongTrending)
			{
				return Hold("Strong trend - avoid mean reversion");
			}

			// Find support/resistance levels
			var srLevels = Indicators.FindSupportResistance(highs, lows, closes, 50, 0.015m);

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
			var volAnalysis = Indicators.AnalyzeVolume(closes, volumes, 20);

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
			var (adx, diPlus, diMinus) = Indicators.ADXList(highs, lows, closes, 14);
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
			var volAnalysis = Indicators.AnalyzeVolume(closes, volumes, 20);

			if (volAnalysis.VolumeRatio < 1.5m)
				return Hold($"Insufficient volume for breakout (ratio={volAnalysis.VolumeRatio:F2})");

			// Candlestick pattern confirmation
			var patterns = Indicators.RecognizePatterns(opens, highs, lows, closes);
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
			var volAnalysis = Indicators.AnalyzeVolume(closes, volumes, 20);

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
			var regime = Indicators.DetectMarketRegime(closes, highs, lows);
			bool correctRegime = (proposedDirection == "Buy" && regime.IsTrendingUp) ||
								(proposedDirection == "Sell" && regime.IsTrendingDown);
			score += correctRegime ? 25m * regime.RegimeConfidence : 0m;

			// 2. Multi-Timeframe Alignment (20%)
			var mtf = Indicators.AnalyzeMultiTimeframe(closes, highs, lows);
			bool mtfAligned = (proposedDirection == "Buy" && mtf.CurrentTFTrend == "Up" && mtf.IsAligned) ||
							 (proposedDirection == "Sell" && mtf.CurrentTFTrend == "Down" && mtf.IsAligned);
			score += mtfAligned ? 20m * mtf.Confidence : 0m;

			// 3. Volume Confirmation (15%)
			if (volumes != null && volumes.Count == closes.Count)
			{
				var volAnalysis = Indicators.AnalyzeVolume(closes, volumes, 20);
				bool volConfirm = (proposedDirection == "Buy" && volAnalysis.IsAccumulation) ||
								 (proposedDirection == "Sell" && volAnalysis.IsDistribution);
				score += volConfirm ? 15m * volAnalysis.VolumeStrength : 0m;
			}

			// 4. Support/Resistance Proximity (15%)
			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);
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
			var patterns = Indicators.RecognizePatterns(opens, highs, lows, closes);
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

		// ═══ HELPER METHODS ═══

		private static decimal CalculateVWAP(List<decimal> closes, List<decimal> highs,
					List<decimal> lows, List<decimal> volumes, int lookback)
		{
			decimal cumVolPrice = 0m;
			decimal cumVol = 0m;

			for (int i = Math.Max(0, closes.Count - lookback); i < closes.Count; i++)
			{
				decimal typical = (highs[i] + lows[i] + closes[i]) / 3m;
				cumVolPrice += typical * volumes[i];
				cumVol += volumes[i];
			}

			return cumVol > 0 ? cumVolPrice / cumVol : 0m;
		}

		private static (decimal tenkan, decimal kijun, decimal senkouA, decimal senkouB)
					CalculateIchimoku(List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			int idx = closes.Count - 1;
			decimal tenkan = (highs.Skip(idx - 8).Take(9).Max() + lows.Skip(idx - 8).Take(9).Min()) / 2m;
			decimal kijun = (highs.Skip(idx - 25).Take(26).Max() + lows.Skip(idx - 25).Take(26).Min()) / 2m;
			decimal senkouA = (tenkan + kijun) / 2m;
			decimal senkouB = (highs.Skip(idx - 51).Take(52).Max() + lows.Skip(idx - 51).Take(52).Min()) / 2m;
			return (tenkan, kijun, senkouA, senkouB);
		}

		private static List<(int index, decimal value, bool isHigh)> FindSwingPoints(
					List<decimal> highs, List<decimal> lows, List<decimal> closes, int lookback)
		{
			var swings = new List<(int index, decimal value, bool isHigh)>();

			for (int i = lookback; i < closes.Count - lookback; i++)
			{
				// Check for swing high
				bool isSwingHigh = true;
				for (int j = i - lookback; j < i + lookback; j++)
				{
					if (j != i && highs[j] >= highs[i])
					{
						isSwingHigh = false;
						break;
					}
				}
				if (isSwingHigh) swings.Add((i, highs[i], true));

				// Check for swing low
				bool isSwingLow = true;
				for (int j = i - lookback; j < i + lookback; j++)
				{
					if (j != i && lows[j] <= lows[i])
					{
						isSwingLow = false;
						break;
					}
				}
				if (isSwingLow) swings.Add((i, lows[i], false));
			}

			return swings.OrderBy(s => s.index).ToList();
		}

		private static List<decimal> CalculateMFI(List<decimal> highs, List<decimal> lows,
					List<decimal> closes, List<decimal> volumes, int period)
		{
			var mfi = new List<decimal>();
			var typicalPrices = new List<decimal>();
			var moneyFlows = new List<decimal>();

			for (int i = 0; i < closes.Count; i++)
			{
				decimal tp = (highs[i] + lows[i] + closes[i]) / 3m;
				typicalPrices.Add(tp);
				moneyFlows.Add(tp * volumes[i]);
			}

			for (int i = period; i < closes.Count; i++)
			{
				decimal positiveFlow = 0m;
				decimal negativeFlow = 0m;

				for (int j = i - period + 1; j <= i; j++)
				{
					if (typicalPrices[j] > typicalPrices[j - 1])
						positiveFlow += moneyFlows[j];
					else if (typicalPrices[j] < typicalPrices[j - 1])
						negativeFlow += moneyFlows[j];
				}

				decimal mfiValue = negativeFlow == 0 ? 100m :
					100m - (100m / (1m + (positiveFlow / negativeFlow)));

				mfi.Add(Math.Round(mfiValue, 2));
			}

			return mfi;
		}

		private static List<decimal> CalculateParabolicSAR(List<decimal> highs, List<decimal> lows,
					decimal acceleration, decimal maximum)
		{
			var sar = new List<decimal>();
			bool isLong = true;
			decimal af = acceleration;
			decimal ep = highs[0];
			decimal sarValue = lows[0];

			sar.Add(sarValue);

			for (int i = 1; i < highs.Count; i++)
			{
				sarValue = sarValue + af * (ep - sarValue);

				if (isLong)
				{
					if (lows[i] < sarValue)
					{
						isLong = false;
						sarValue = ep;
						ep = lows[i];
						af = acceleration;
					}
					else
					{
						if (highs[i] > ep)
						{
							ep = highs[i];
							af = Math.Min(af + acceleration, maximum);
						}
					}
				}
				else
				{
					if (highs[i] > sarValue)
					{
						isLong = true;
						sarValue = ep;
						ep = highs[i];
						af = acceleration;
					}
					else
					{
						if (lows[i] < ep)
						{
							ep = lows[i];
							af = Math.Min(af + acceleration, maximum);
						}
					}
				}

				sar.Add(Math.Round(sarValue, 4));
			}

			return sar;
		}

	}
}