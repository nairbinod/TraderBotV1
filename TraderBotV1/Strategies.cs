namespace TraderBotV1
{

	/*
        RSI (14) below 30 → Oversold → Buy. Uses 14-day period.
        EMA (9) crosses above EMA (21) → Bullish crossover → Buy.
        MACD(12,26,9) shows bullish divergence → Buy.
        Bollinger Bands (20,2) price touches lower band → Buy.
        ATR (14) breakout above previous high + ATR → Buy.
        
        Four Strategies:
        1. EMA + RSI Strategy
        2. Bollinger Bands Mean Reversion
        3. ATR Breakout Strategy
        4. MACD Divergence Strategy
        Combine signals: If 2 or more strategies signal Buy, then Buy. If 2 or more signal Sell, then Sell. Else Hold.

    */

	public record StrategySignal(string Signal, decimal Strength, string Reason);

	public static class Strategies
	{
		private static StrategySignal Hold(string reason = "no setup") =>
			new("Hold", 0m, reason);

		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));

		// ─────────────────────────────────────────────────────────────────────────────
		// 1) EMA Crossover + RSI Filter (trend-following with momentum gate)
		// ─────────────────────────────────────────────────────────────────────────────
		public static StrategySignal EmaRsi(
			List<decimal> closes,
			List<decimal> emaShort,  // e.g., 9
			List<decimal> emaLong,   // e.g., 21
			int rsiPeriod = 14,
			(decimal min, decimal max)? longRsiBand = null,   // default (50..70)
			(decimal min, decimal max)? shortRsiBand = null   // default (30..50)
		)
		{
			if (closes.Count < 3 || emaShort.Count < 3 || emaLong.Count < 3)
				return Hold("insufficient data");

			longRsiBand ??= (50m, 70m);
			shortRsiBand ??= (30m, 50m);

			var rsiNow = Indicators.RSI(closes, rsiPeriod);
			var rsiPrev = Indicators.RSI(closes.Take(closes.Count - 1).ToList(), rsiPeriod);

			bool crossUp = emaShort[^1] > emaLong[^1] && emaShort[^2] <= emaLong[^2];
			bool crossDown = emaShort[^1] < emaLong[^1] && emaShort[^2] >= emaLong[^2];

			// ➕ Slope confirmation (reduce whipsaws)
			bool slopeUp = emaShort[^1] > emaShort[^2];
			bool slopeDown = emaShort[^1] < emaShort[^2];

			// ➕ RSI *cross* of 50 (momentum shift), not just “being above/below”
			bool rsiCrossedUp = rsiPrev <= 50m && rsiNow > 50m;
			bool rsiCrossedDown = rsiPrev >= 50m && rsiNow < 50m;

			var price = closes[^1];
			var emaGap = Math.Abs(emaShort[^1] - emaLong[^1]);
			var gapRel = price > 0 ? emaGap / price : 0m;

			if (crossUp && slopeUp && rsiCrossedUp && rsiNow > longRsiBand.Value.min && rsiNow < longRsiBand.Value.max)
			{
				// strength: blend of normalized EMA gap & RSI distance above 50
				var strength = Clamp01(gapRel * 6m * 0.5m + Clamp01((rsiNow - 50m) / 25m) * 0.6m);
				return new("Buy", strength, $"EMA↑ + slope + RSI↑50; rsi={rsiNow:F1}; gap={emaGap:F4}");
			}

			if (crossDown && slopeDown && rsiCrossedDown && rsiNow < shortRsiBand.Value.max && rsiNow > shortRsiBand.Value.min)
			{
				var strength = Clamp01(gapRel * 6m * 0.5m + Clamp01((50m - rsiNow) / 25m) * 0.6m);
				return new("Sell", strength, $"EMA↓ + slope + RSI↓50; rsi={rsiNow:F1}; gap={emaGap:F4}");
			}

			return Hold($"no cross/confirm; rsi={rsiNow:F1}");
		}

		// ─────────────────────────────────────────────────────────────────────────────
		// 2) Bollinger Band Mean Reversion (contrarian with trend+exhaustion filters)
		// ─────────────────────────────────────────────────────────────────────────────
		public static StrategySignal BollingerMeanReversion(
			List<decimal> closes,
			List<decimal?> upper,
			List<decimal?> lower,
			List<decimal?>? middle = null,
			int rsiPeriod = 14)
		{
			if (closes.Count == 0 || upper.Count == 0 || lower.Count == 0)
				return Hold("no data");

			var price = closes[^1];
			var ub = upper[^1];
			var lb = lower[^1];

			if (ub is null || lb is null) return Hold("bands not ready");

			var bandWidth = Math.Max(1e-8m, (ub.Value - lb.Value));
			if (bandWidth <= 0) return Hold("invalid bands");

			// ➕ Ignore ultra-narrow bands (noisy range)
			if (bandWidth / Math.Max(1e-8m, price) < 0.005m) // <0.5%
				return Hold("bands too narrow");

			// ➕ Trend context via middle-band slope (avoid counter-trend knives)
			decimal midSlope = 0m;
			if (middle is not null && middle.Count >= 3 && middle[^1].HasValue && middle[^3].HasValue)
				midSlope = middle[^1]!.Value - middle[^3]!.Value;

			// ➕ RSI exhaustion
			var rsi = Indicators.RSI(closes, rsiPeriod);

			if (price <= lb.Value)
			{
				// only buy if trend is not strongly down
				if (midSlope < -price * 0.002m) return Hold("downtrend too strong");
				if (rsi >= 30m) return Hold("no RSI exhaustion");

				var depth = Clamp01((lb.Value - price) / bandWidth * 2m);
				// boost strength when RSI is deeper into oversold
				var strength = Clamp01(depth * 0.7m + Clamp01((30m - rsi) / 20m) * 0.6m);
				return new("Buy", strength, $"<=LB + RSI<30; z~{-depth:F3}; midSlope={midSlope:F4}");
			}

			if (price >= ub.Value)
			{
				if (midSlope > price * 0.002m) return Hold("uptrend too strong");
				if (rsi <= 70m) return Hold("no RSI exhaustion");

				var depth = Clamp01((price - ub.Value) / bandWidth * 2m);
				var strength = Clamp01(depth * 0.7m + Clamp01((rsi - 70m) / 20m) * 0.6m);
				return new("Sell", strength, $">=UB + RSI>70; z~{depth:F3}; midSlope={midSlope:F4}");
			}

			return Hold("inside bands");
		}

		// ─────────────────────────────────────────────────────────────────────────────
		// 3) ATR Volatility Breakout (requires expansion + momentum confirmation)
		// ─────────────────────────────────────────────────────────────────────────────
		public static StrategySignal AtrBreakout(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> atr,        // Wilder ATR list (aligned)
			decimal k = 0.5m,
			int rsiPeriod = 14)
		{
			if (closes.Count < 3 || highs.Count < 3 || lows.Count < 3 || atr.Count < 3)
				return Hold("insufficient data");

			var last = closes[^1];
			var prevHigh = highs[^2];
			var prevLow = lows[^2];
			var a = atr[^1];
			var a0 = atr[^2];

			if (a <= 0) return Hold("atr<=0");

			// thresholds
			var buyThresh = prevHigh + k * a;
			var sellThresh = prevLow - k * a;

			// ➕ ATR expansion
			bool atrRising = a > a0;

			// ➕ RSI confirmation
			var rsi = Indicators.RSI(closes, rsiPeriod);

			if (last > buyThresh && atrRising && rsi > 55m)
			{
				var strength = Clamp01((last - buyThresh) / Math.Max(a, 1e-8m)) * 0.7m
							 + Clamp01((rsi - 55m) / 20m) * 0.6m;
				return new("Buy", Clamp01(strength), $"BO↑ last>{buyThresh:F2}; ATR↑; RSI={rsi:F1}");
			}

			if (last < sellThresh && atrRising && rsi < 45m)
			{
				var strength = Clamp01((sellThresh - last) / Math.Max(a, 1e-8m)) * 0.7m
							 + Clamp01((45m - rsi) / 20m) * 0.6m;
				return new("Sell", Clamp01(strength), $"BD↓ last<{sellThresh:F2}; ATR↑; RSI={rsi:F1}");
			}

			return Hold("no breakout/confirm");
		}

		// ─────────────────────────────────────────────────────────────────────────────
		// 4) MACD Divergence (5-bar swing points + histogram + long-trend filter)
		// ─────────────────────────────────────────────────────────────────────────────
		public static StrategySignal MacdDivergence(
			List<decimal> closes,
			List<decimal> macd,
			List<decimal> signal,
			List<decimal> hist)
		{
			if (closes.Count < 10 || macd.Count < 10 || signal.Count < 10 || hist.Count < 10)
				return Hold("insufficient data");

			// Find swing lows/highs over last 5 bars
			// (simple local extrema; for robustness you can widen to 7)
			int N = 5;
			int start = Math.Max(0, closes.Count - N);

			var spanClose = closes.GetRange(start, closes.Count - start);
			var spanMacd = macd.GetRange(start, macd.Count - start);
			var spanHist = hist.GetRange(start, hist.Count - start);

			int idxCloseMin = IndexOfMin(spanClose) + start;
			int idxCloseMax = IndexOfMax(spanClose) + start;
			int idxMacdMin = IndexOfMin(spanMacd) + start;
			int idxMacdMax = IndexOfMax(spanMacd) + start;

			// Long-term trend filter with EMA50 vs EMA200
			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = Indicators.EMAList(closes, 200);
			bool longUp = ema50.Count == closes.Count && ema200.Count == closes.Count && ema50[^1] > ema200[^1];
			bool longDown = ema50.Count == closes.Count && ema200.Count == closes.Count && ema50[^1] < ema200[^1];

			// Histogram tone / recent zero-cross confirmation
			bool histUpTone = spanHist[^1] > 0 && spanHist[^1] > spanHist[Math.Max(0, spanHist.Count - 2)];
			bool histDownTone = spanHist[^1] < 0 && spanHist[^1] < spanHist[Math.Max(0, spanHist.Count - 2)];

			// Bullish divergence: price makes lower low while MACD makes higher low
			bool priceLL = closes[^1] < closes[idxCloseMin] || idxCloseMin == closes.Count - 1;
			bool macdHL = macd[^1] > macd[idxMacdMin] || idxMacdMin == macd.Count - 1;

			if (priceLL && macdHL && histUpTone && longUp)
			{
				var mag = Math.Abs(macd[^1] - macd[Math.Max(0, macd.Count - 2)]);
				var strength = Clamp01((decimal)mag * 2m);
				return new("Buy", strength, $"bull div (5b) + hist↑ + trend↑; ΔMACD={mag:F4}");
			}

			// Bearish divergence: price makes higher high while MACD makes lower high
			bool priceHH = closes[^1] > closes[idxCloseMax] || idxCloseMax == closes.Count - 1;
			bool macdLH = macd[^1] < macd[idxMacdMax] || idxMacdMax == macd.Count - 1;

			if (priceHH && macdLH && histDownTone && longDown)
			{
				var mag = Math.Abs(macd[^1] - macd[Math.Max(0, macd.Count - 2)]);
				var strength = Clamp01((decimal)mag * 2m);
				return new("Sell", strength, $"bear div (5b) + hist↓ + trend↓; ΔMACD={mag:F4}");
			}

			return Hold("no divergence/confirm");
		}

		// ─────────────────────────────────────────────────────────────────────────────
		// Helpers
		// ─────────────────────────────────────────────────────────────────────────────
		private static int IndexOfMin(List<decimal> list)
		{
			int idx = 0;
			for (int i = 1; i < list.Count; i++)
				if (list[i] < list[idx]) idx = i;
			return idx;
		}

		private static int IndexOfMax(List<decimal> list)
		{
			int idx = 0;
			for (int i = 1; i < list.Count; i++)
				if (list[i] > list[idx]) idx = i;
			return idx;
		}
	}
}
