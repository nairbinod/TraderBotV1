using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public record StrategySignal(string Signal, decimal Strength, string Reason);

	public static class Strategies
	{
		private static StrategySignal Hold(string reason = "no setup") => new("Hold", 0m, reason);
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));

		// ─────────────────────────────────────────────────────────────
		// 1) EMA Crossover + RSI Filter (with validation)
		// ─────────────────────────────────────────────────────────────
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

		// ─────────────────────────────────────────────────────────────
		// 2) Bollinger Mean Reversion (with trend filter)
		// ─────────────────────────────────────────────────────────────
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

		// ─────────────────────────────────────────────────────────────
		// 3) ATR Volatility Breakout (with validation)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal AtrBreakout(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> atr,
			int emaFast = 9,
			int emaSlow = 21,
			int rsiPeriod = 14,
			decimal k = 1.2m)
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

			// Volatility check
			decimal volRatio = currAtr / Math.Max(price, 1e-8m);
			if (volRatio < 0.004m)
				return Hold($"Insufficient volatility: {volRatio:P2}");

			if (context.VolatilityRatio > 3m)
				return Hold($"Extreme volatility spike: {context.VolatilityRatio:F2}x");

			var emaFastList = Indicators.EMAList(closes, emaFast);
			var emaSlowList = Indicators.EMAList(closes, emaSlow);
			var rsiList = Indicators.RSIList(closes, rsiPeriod);

			bool trendUp = emaFastList[idx] > emaSlowList[idx];
			bool trendDown = emaFastList[idx] < emaSlowList[idx];
			decimal rsi = rsiList.Count > idx ? rsiList[idx] : 50m;

			decimal buyThresh = prevHigh + k * currAtr;
			decimal sellThresh = prevLow - k * currAtr;

			// BUY breakout
			if (price > buyThresh && trendUp)
			{
				// Validate breakout
				if (closes[idx - 1] > buyThresh)
					return Hold("No clear breakout (already above threshold)");

				if (rsi > 75m)
					return Hold($"Overbought RSI: {rsi:F1}");

				// Momentum confirmation
				bool momentum = closes[idx] > closes[idx - 2];
				if (!momentum)
					return Hold("Weak momentum on breakout");

				decimal breakoutStrength = (price - buyThresh) / currAtr;
				decimal strength = Clamp01(breakoutStrength * 0.3m + 0.5m);

				return new("Buy", strength,
					$"ATR breakout ↑ validated (${price:F2}>${buyThresh:F2}, RSI={rsi:F1})");
			}

			// SELL breakdown
			if (price < sellThresh && trendDown)
			{
				if (closes[idx - 1] < sellThresh)
					return Hold("No clear breakdown (already below threshold)");

				if (rsi < 25m)
					return Hold($"Oversold RSI: {rsi:F1}");

				bool momentum = closes[idx] < closes[idx - 2];
				if (!momentum)
					return Hold("Weak momentum on breakdown");

				decimal breakdownStrength = (sellThresh - price) / currAtr;
				decimal strength = Clamp01(breakdownStrength * 0.3m + 0.5m);

				return new("Sell", strength,
					$"ATR breakdown ↓ validated (${price:F2}<${sellThresh:F2}, RSI={rsi:F1})");
			}

			return Hold($"No breakout (price ${price:F2}, buy>${buyThresh:F2}, sell<${sellThresh:F2})");
		}

		// ─────────────────────────────────────────────────────────────
		// 4) MACD Divergence (with validation)
		// ─────────────────────────────────────────────────────────────
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
	}
}