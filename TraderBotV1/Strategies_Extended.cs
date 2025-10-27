using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class StrategiesExtended
	{
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));
		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);

		// ─────────────────────────────────────────────────────────────
		// 1) ADX Trend Filter (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal AdxFilter(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			int period = 14,
			decimal threshold = 25m)
		{
			var (adx, diPlus, diMinus) = IndicatorsExtended.ADXList(highs, lows, closes, period);
			if (adx.Count < 5) return Hold("ADX insufficient data");

			int idx = adx.Count - 1;
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// Skip in low volatility
			if (context.RecentRange < 0.008m)
				return Hold($"Low volatility: range={context.RecentRange:P2}");

			// Check for uptrend
			if (diPlus[idx] > diMinus[idx] + 5m)
			{
				var validation = ExtendedSignalValidator.ValidateADX(adx, diPlus, diMinus, idx, "Buy", threshold);
				if (!validation.IsValid)
					return Hold($"ADX buy rejected: {validation.Reason}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for downtrend
			if (diMinus[idx] > diPlus[idx] + 5m)
			{
				var validation = ExtendedSignalValidator.ValidateADX(adx, diPlus, diMinus, idx, "Sell", threshold);
				if (!validation.IsValid)
					return Hold($"ADX sell rejected: {validation.Reason}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"ADX neutral (DI+={diPlus[idx]:F1}, DI-={diMinus[idx]:F1})");
		}

		// ─────────────────────────────────────────────────────────────
		// 2) Volume Confirmation (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal VolumeConfirm(
			List<decimal> closes,
			List<decimal> volumes,
			int period = 20,
			decimal spikeMultiple = 1.5m)
		{
			if (volumes == null || volumes.Count < closes.Count || volumes.Count < period + 5)
				return Hold("Volume data insufficient");

			int idx = closes.Count - 1;
			var validation = ExtendedSignalValidator.ValidateVolumeSpike(volumes, closes, idx, spikeMultiple);

			if (!validation.IsValid)
				return Hold(validation.Reason);

			bool upBar = closes[idx] > closes[idx - 1];
			string direction = upBar ? "Buy" : "Sell";

			return new(direction, validation.Confidence, validation.Reason);
		}

		// ─────────────────────────────────────────────────────────────
		// 3) CCI Reversion (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal CciReversion(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			int period = 20)
		{
			var cci = IndicatorsExtended.CCIList(highs, lows, closes, period);
			if (cci.Count < 5) return Hold("CCI insufficient data");

			int idx = cci.Count - 1;
			decimal cciNow = cci[idx];
			decimal cciPrev = cci[idx - 1];

			// Check for buy signal
			if (cciPrev <= -100m && cciNow > -100m)
			{
				var validation = ExtendedSignalValidator.ValidateCCI(cci, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"CCI buy rejected: {validation.Reason}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for sell signal
			if (cciPrev >= 100m && cciNow < 100m)
			{
				var validation = ExtendedSignalValidator.ValidateCCI(cci, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"CCI sell rejected: {validation.Reason}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"CCI neutral ({cciNow:F0})");
		}

		// ─────────────────────────────────────────────────────────────
		// 4) Donchian Breakout (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal DonchianBreakout(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			int period = 20)
		{
			if (closes.Count < period + 10)
				return Hold("Donchian insufficient data");

			var (upper, lower) = IndicatorsExtended.DonchianChannel(highs, lows, period);
			var atr = IndicatorsExtended.ATRList(highs, lows, closes, 14);
			var rsi = Indicators.RSIList(closes, 14);

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal u = upper[idx];
			decimal l = lower[idx];

			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// Check for breakout above upper band
			if (price > u)
			{
				var validation = ExtendedSignalValidator.ValidateDonchianBreakout(
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
				var validation = ExtendedSignalValidator.ValidateDonchianBreakout(
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

		// ─────────────────────────────────────────────────────────────
		// 5) Pivot Reversal (with trend context)
		// ─────────────────────────────────────────────────────────────
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
			var (P, R1, S1) = IndicatorsExtended.PivotPoints(prevHigh, prevLow, prevClose);

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

		// ─────────────────────────────────────────────────────────────
		// 6) StochRSI Reversal (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal StochRsiReversal(
			List<decimal> closes,
			int rsiPeriod = 14,
			int stochPeriod = 14,
			int smoothK = 3,
			int smoothD = 3)
		{
			var (k, d) = IndicatorsExtended.StochRSIList(closes, rsiPeriod, stochPeriod, smoothK, smoothD);
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
				var validation = ExtendedSignalValidator.ValidateStochRSI(k, d, rsi, idx, "Buy");
				if (!validation.IsValid)
					return Hold($"StochRSI buy rejected: {validation.Reason}");

				return new("Buy", validation.Confidence, validation.Reason);
			}

			// Check for sell signal
			bool crossDown = kNow < dNow && kPrev >= dPrev;
			if (crossDown && kNow > 0.5m)
			{
				var validation = ExtendedSignalValidator.ValidateStochRSI(k, d, rsi, idx, "Sell");
				if (!validation.IsValid)
					return Hold($"StochRSI sell rejected: {validation.Reason}");

				return new("Sell", validation.Confidence, validation.Reason);
			}

			return Hold($"StochRSI no signal (K={kNow:F2}, D={dNow:F2})");
		}

		// ─────────────────────────────────────────────────────────────
		// 7) EMA 200 Regime Filter (with context)
		// ─────────────────────────────────────────────────────────────
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
	}
}