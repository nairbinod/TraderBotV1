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
		// 1) EMA Crossover + RSI Filter
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal EmaRsi(
			List<decimal> closes,
			int fast = 9,
			int slow = 21,
			int rsiPeriod = 14)
		{
			if (closes.Count < slow + 3) return Hold("insufficient data");

			var emaFast = Indicators.EMAList(closes, fast);
			var emaSlow = Indicators.EMAList(closes, slow);
			var rsiList = Indicators.RSIList(closes, rsiPeriod);
			if (emaFast.Count < 3 || emaSlow.Count < 3 || rsiList.Count == 0)
				return Hold("not ready");

			var rsiNow = rsiList[^1];
			var rsiPrev = rsiList[^2];

			bool crossUp = emaFast[^1] > emaSlow[^1] && emaFast[^2] <= emaSlow[^2];
			bool crossDown = emaFast[^1] < emaSlow[^1] && emaFast[^2] >= emaSlow[^2];

			bool slopeUp = emaFast[^1] > emaFast[^2] && emaFast[^2] > emaFast[^3];
			bool slopeDown = emaFast[^1] < emaFast[^2] && emaFast[^2] < emaFast[^3];

			var price = closes[^1];
			var emaGap = Math.Abs(emaFast[^1] - emaSlow[^1]);
			var gapRel = price > 0 ? emaGap / price : 0m;

			bool strongGap = gapRel > 0.002m;
			bool rsiCrossedUp = rsiPrev <= 50m && rsiNow > 50m;
			bool rsiCrossedDown = rsiPrev >= 50m && rsiNow < 50m;

			if (crossUp && slopeUp && rsiCrossedUp && strongGap && rsiNow > 55m)
			{
				var strength = Clamp01(gapRel * 8m * 0.6m + Clamp01((rsiNow - 55m) / 20m) * 0.4m);
				return new("Buy", strength, $"EMA crossover ↑ & RSI rising ({rsiNow:F1})");
			}

			if (crossDown && slopeDown && rsiCrossedDown && strongGap && rsiNow < 45m)
			{
				var strength = Clamp01(gapRel * 8m * 0.6m + Clamp01((45m - rsiNow) / 20m) * 0.4m);
				return new("Sell", strength, $"EMA crossover ↓ & RSI falling ({rsiNow:F1})");
			}

			return Hold($"No crossover confirmed; RSI={rsiNow:F1}");
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
			if (closes.Count == 0 || upper.Count == 0 || lower.Count == 0)
				return Hold("no data");

			var price = closes[^1];
			var ub = upper[^1];
			var lb = lower[^1];
			if (ub is null || lb is null) return Hold("bands not ready");

			var rsiList = Indicators.RSIList(closes, rsiPeriod);
			if (rsiList.Count == 0) return Hold("rsi not ready");
			var rsi = rsiList[^1];

			// Trend context
			bool uptrend = middle.Count > 3 && middle[^1] > middle[^3];
			bool downtrend = middle.Count > 3 && middle[^1] < middle[^3];

			// BUY near lower band
			if (price <= lb && rsi < 30m && !downtrend)
			{
				var depth = Clamp01((lb.Value - price) / (ub.Value - lb.Value + 1e-8m));
				var strength = Clamp01(depth * 0.6m + Clamp01((30m - rsi) / 20m) * 0.4m);
				return new("Buy", strength, $"Touched lower band + RSI={rsi:F1}");
			}

			// SELL near upper band
			if (price >= ub && rsi > 70m && !uptrend)
			{
				var depth = Clamp01((price - ub.Value) / (ub.Value - lb.Value + 1e-8m));
				var strength = Clamp01(depth * 0.6m + Clamp01((rsi - 70m) / 20m) * 0.4m);
				return new("Sell", strength, $"Touched upper band + RSI={rsi:F1}");
			}

			return Hold("inside band or trend too strong");
		}

		// ─────────────────────────────────────────────────────────────
		// 3) ATR Volatility Breakout
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
			if (closes.Count < emaSlow + 2 || atr.Count < emaSlow + 2)
				return Hold("insufficient data");

			int last = closes.Count - 1;
			decimal lastClose = closes[last];
			decimal prevHigh = highs[last - 1];
			decimal prevLow = lows[last - 1];
			decimal currAtr = atr[last];
			if (currAtr <= 0) return Hold("atr invalid");

			decimal emaFastVal = Indicators.EMA(closes, emaFast);
			decimal emaSlowVal = Indicators.EMA(closes, emaSlow);
			bool trendUp = emaFastVal > emaSlowVal;
			bool trendDown = emaFastVal < emaSlowVal;

			var rsiList = Indicators.RSIList(closes, rsiPeriod);
			decimal rsi = rsiList.Count > last ? rsiList[last] : 50m;

			// breakout levels
			decimal buyThresh = prevHigh + k * currAtr;
			decimal sellThresh = prevLow - k * currAtr;

			// ATR-based volatility normalization
			decimal volRatio = currAtr / Math.Max(lastClose, 1e-8m);
			bool sufficientVol = volRatio > 0.005m; // at least 0.5% ATR

			// RSI confirmation
			bool momentumOK = rsi > 50m && rsi < 70m;

			// Breakout + confirmation + minimum strength trend
			if (lastClose > buyThresh && trendUp && momentumOK && sufficientVol)
			{
				var strength = Clamp01(
					((lastClose - buyThresh) / currAtr) * 0.5m +
					Clamp01((rsi - 50m) / 20m) * 0.3m +
					Clamp01(volRatio * 10m) * 0.2m);

				// Optional: require candle close above both thresholds (confirm breakout)
				if (closes[last] < closes[last - 1]) return Hold("retest pending");

				return new("Buy", strength, $"ATR breakout↑ {lastClose:F2}>{buyThresh:F2}, RSI={rsi:F1}, ATR%={volRatio:P1}");
			}

			if (lastClose < sellThresh && trendDown && rsi < 50m && sufficientVol)
			{
				var strength = Clamp01(
					((sellThresh - lastClose) / currAtr) * 0.5m +
					Clamp01((50m - rsi) / 20m) * 0.3m +
					Clamp01(volRatio * 10m) * 0.2m);

				if (closes[last] > closes[last - 1]) return Hold("retest pending");

				return new("Sell", strength, $"ATR breakdown↓ {lastClose:F2}<{sellThresh:F2}, RSI={rsi:F1}, ATR%={volRatio:P1}");
			}

			return Hold("no confirmed breakout");
		}


		// ─────────────────────────────────────────────────────────────
		// 4) MACD Divergence (trend-aware with smoothed hist)
		// ─────────────────────────────────────────────────────────────
		public static StrategySignal MacdDivergence(
			List<decimal> closes,
			List<decimal> macd,
			List<decimal> signal,
			List<decimal> hist)
		{
			if (closes.Count < 30 || macd.Count < 30 || signal.Count < 30 || hist.Count < 30)
				return Hold("insufficient data");

			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = Indicators.EMAList(closes, 200);
			bool trendUp = ema50[^1] > ema200[^1];
			bool trendDown = ema50[^1] < ema200[^1];

			decimal mag = Math.Abs(macd[^1] - macd[^2]);
			bool histUpTone = hist.TakeLast(3).All(h => h > 0);
			bool histDownTone = hist.TakeLast(3).All(h => h < 0);

			bool bullishDiv = closes[^1] < closes[^3] && macd[^1] > macd[^3] && histUpTone && trendUp;
			bool bearishDiv = closes[^1] > closes[^3] && macd[^1] < macd[^3] && histDownTone && trendDown;

			if (bullishDiv)
			{
				var strength = Clamp01(mag * 2m);
				return new("Buy", strength, $"Bullish MACD divergence; ΔMACD={mag:F3}");
			}

			if (bearishDiv)
			{
				var strength = Clamp01(mag * 2m);
				return new("Sell", strength, $"Bearish MACD divergence; ΔMACD={mag:F3}");
			}

			return Hold("no divergence");
		}
	}
}
