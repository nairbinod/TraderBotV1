using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class IndicatorsExtended
	{
		// --- StochRSI ---
		// --- StochRSI (False-signal resistant version) ---
		public static (List<decimal> k, List<decimal> d) StochRSIList(
			List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
		{
			int n = closes?.Count ?? 0;
			var filteredK = new List<decimal>(new decimal[n]);
			var filteredD = new List<decimal>(new decimal[n]);
			if (n < rsiPeriod + stochPeriod + Math.Max(smoothK, smoothD))
				return (filteredK, filteredD);

			// ✅ Compute base StochRSI (internal core logic, same as your original)
			var rsiVals = Indicators.RSIList(closes, rsiPeriod);
			int rsiCount = rsiVals?.Count ?? 0;
			if (rsiCount < stochPeriod + 1)
				return (filteredK, filteredD);

			int validLen = Math.Min(n, rsiCount);
			var kRaw = new decimal[validLen];
			var dRaw = new decimal[validLen];

			for (int i = stochPeriod - 1; i < validLen; i++)
			{
				int start = Math.Max(0, i - stochPeriod + 1);
				var segment = rsiVals.Skip(start).Take(stochPeriod).ToList();
				decimal minR = segment.Min();
				decimal maxR = segment.Max();
				decimal denom = Math.Max(maxR - minR, 1e-8m);
				kRaw[i] = Math.Clamp((rsiVals[i] - minR) / denom, 0m, 1m);
			}

			// Smooth K
			for (int i = 0; i < validLen; i++)
			{
				int start = Math.Max(0, i - smoothK + 1);
				filteredK[i] = Math.Round(kRaw.Skip(start).Take(i - start + 1).Average(), 4);
			}

			// Smooth D
			for (int i = 0; i < validLen; i++)
			{
				int start = Math.Max(0, i - smoothD + 1);
				filteredD[i] = Math.Round(filteredK.Skip(start).Take(i - start + 1).Average(), 4);
			}

			// ✅ Now apply noise suppression filter
			var atr = Indicators.ATRList(closes, closes, closes, 14);

			for (int i = 1; i < validLen; i++)
			{
				// Volatility filter
				bool volatilityOK = (i < atr.Count) && (atr[i] > closes[i] * 0.005m); // >0.5%

				// RSI confirmation
				bool rsiConfirm =
					(rsiVals[i] < 35m && filteredK[i] < 0.2m) || // oversold + low K
					(rsiVals[i] > 65m && filteredK[i] > 0.8m);   // overbought + high K

				// Smooth cross
				bool smoothCross = Math.Abs(filteredK[i] - filteredD[i]) > 0.05m;

				if (!(volatilityOK && rsiConfirm && smoothCross))
				{
					// damp noise — pull toward previous
					filteredK[i] = Math.Round((filteredK[i - 1] * 0.7m + filteredK[i] * 0.3m), 4);
					filteredD[i] = Math.Round((filteredD[i - 1] * 0.7m + filteredD[i] * 0.3m), 4);
				}
			}

			// Ensure output length consistency
			if (filteredK.Count < n) filteredK.AddRange(Enumerable.Repeat(0m, n - filteredK.Count));
			if (filteredD.Count < n) filteredD.AddRange(Enumerable.Repeat(0m, n - filteredD.Count));

			return (filteredK, filteredD);
		}

		// --- ADX (False-signal resistant version) ---
		public static (List<decimal> adx, List<decimal> diPlus, List<decimal> diMinus) ADXList(
	List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			int n = Math.Min(highs.Count, Math.Min(lows.Count, closes.Count));
			var adx = new List<decimal>(new decimal[n]);
			var diPlusList = new List<decimal>(new decimal[n]);
			var diMinusList = new List<decimal>(new decimal[n]);
			if (n < period + 2) return (adx, diPlusList, diMinusList);

			var tr = new decimal[n];
			var dmPlus = new decimal[n];
			var dmMinus = new decimal[n];

			// Step 1: True Range and Directional Movement
			for (int i = 1; i < n; i++)
			{
				decimal upMove = highs[i] - highs[i - 1];
				decimal downMove = lows[i - 1] - lows[i];

				dmPlus[i] = (upMove > downMove && upMove > 0) ? upMove : 0m;
				dmMinus[i] = (downMove > upMove && downMove > 0) ? downMove : 0m;

				decimal hl = highs[i] - lows[i];
				decimal hc = Math.Abs(highs[i] - closes[i - 1]);
				decimal lc = Math.Abs(lows[i] - closes[i - 1]);
				tr[i] = Math.Max(hl, Math.Max(hc, lc));
			}

			// Step 2: Wilder smoothing
			decimal atr = tr.Skip(1).Take(period).Average();
			decimal smPlus = dmPlus.Skip(1).Take(period).Average();
			decimal smMinus = dmMinus.Skip(1).Take(period).Average();

			var dx = new decimal[n];
			for (int i = period + 1; i < n; i++)
			{
				atr = (atr * (period - 1) + tr[i]) / period;
				smPlus = (smPlus * (period - 1) + dmPlus[i]) / period;
				smMinus = (smMinus * (period - 1) + dmMinus[i]) / period;

				if (atr <= 1e-8m) { dx[i] = 0m; continue; }

				decimal diPlus = 100m * (smPlus / atr);
				decimal diMinus = 100m * (smMinus / atr);

				// ✅ Save DI+ and DI- for external directional use
				diPlusList[i] = diPlus;
				diMinusList[i] = diMinus;

				// ✅ Require minimum trend separation (filter noise)
				if (Math.Abs(diPlus - diMinus) < 15m)
				{
					dx[i] = 0m;
					continue;
				}

				decimal sum = diPlus + diMinus;
				dx[i] = (sum == 0) ? 0m : 100m * Math.Abs(diPlus - diMinus) / sum;
			}

			// Step 3: Smooth ADX (less reactive)
			int adxStart = Math.Min(period * 2 - 1, n - 1);
			decimal firstAdx = dx.Skip(period).Take(period).DefaultIfEmpty(0m).Average();
			adx[adxStart] = firstAdx;

			for (int i = adxStart + 1; i < n; i++)
				adx[i] = (adx[i - 1] * (period - 1) + dx[i]) / period;

			// Step 4: Additional smoothing & normalization
			for (int i = 1; i < n; i++)
				adx[i] = Math.Round((adx[i] * 0.6m + adx[i - 1] * 0.4m), 2);

			// Step 5: Optional dampening in low volatility
			decimal avgATR = atr / Math.Max(closes.Last(), 1e-8m);
			if (avgATR < 0.008m) // only 0.8% volatility
				adx = adx.Select(v => Math.Round(v * 0.5m, 2)).ToList();

			// Step 6: Final false-signal suppression
			for (int i = 0; i < n; i++)
			{
				if (adx[i] < 20m) adx[i] = 0m; // ignore weak trends
				if (Math.Abs(diPlusList[i] - diMinusList[i]) < 10m) adx[i] *= 0.5m; // low separation dampening
			}

			return (adx, diPlusList, diMinusList);
		}



		// --- CCI ---
		public static List<decimal> CCIList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
		{
			int n = Math.Min(highs.Count, Math.Min(lows.Count, closes.Count));
			var cci = Enumerable.Repeat(0m, n).ToList();
			if (n < period + 1) return cci;

			var tp = new decimal[n];
			for (int i = 0; i < n; i++)
				tp[i] = (highs[i] + lows[i] + closes[i]) / 3m;

			for (int i = period - 1; i < n; i++)
			{
				int start = Math.Max(0, i - period + 1);
				var slice = tp.Skip(start).Take(Math.Min(period, n - start)).ToList();
				decimal sma = slice.Average();
				decimal meanDev = slice.Select(x => Math.Abs(x - sma)).DefaultIfEmpty(0m).Average();
				if (meanDev <= 1e-8m) { cci[i] = 0m; continue; }
				cci[i] = Math.Round((tp[i] - sma) / (0.015m * meanDev), 2);
			}

			for (int i = 1; i < n; i++)
				cci[i] = Math.Round(cci[i - 1] * 0.2m + cci[i] * 0.8m, 2);

			return cci;
		}

		// --- Donchian Channel ---
		// --- Donchian Channel (False-signal resistant version) ---
		public static (List<decimal> upper, List<decimal> lower) DonchianChannel(
			List<decimal> highs, List<decimal> lows, int period = 20, decimal baseBuffer = 0.002m)
		{
			int n = Math.Min(highs.Count, lows.Count);
			var upper = new List<decimal>(new decimal[n]);
			var lower = new List<decimal>(new decimal[n]);
			if (n < period + 1) return (upper, lower);

			for (int i = period - 1; i < n; i++)
			{
				int start = Math.Max(0, i - period + 1);
				var highSlice = highs.Skip(start).Take(period).ToList();
				var lowSlice = lows.Skip(start).Take(period).ToList();

				decimal highMax = highSlice.Max();
				decimal lowMin = lowSlice.Min();
				decimal range = Math.Max(highMax - lowMin, 1e-8m);

				// --- Adaptive buffer: widen in tight ranges ---
				decimal dynamicBuffer = baseBuffer;
				if (range / Math.Max(highMax, 1e-8m) < 0.01m) // <1% price range → tighten filter
					dynamicBuffer *= 2.5m; // require stronger breakout

				upper[i] = Math.Round(highMax + (range * dynamicBuffer), 4);
				lower[i] = Math.Round(lowMin - (range * dynamicBuffer), 4);
			}

			// --- EMA-like smoothing to reduce whipsaws ---
			const decimal alpha = 0.25m; // smoother than simple average
			for (int i = 1; i < n; i++)
			{
				upper[i] = Math.Round(upper[i - 1] * (1 - alpha) + upper[i] * alpha, 4);
				lower[i] = Math.Round(lower[i - 1] * (1 - alpha) + lower[i] * alpha, 4);
			}

			// --- Trend slope filter (optional) ---
			// Flat or contracting channels imply sideways markets — suppress false breakouts.
			for (int i = 2; i < n; i++)
			{
				var slopeU = upper[i] - upper[i - 2];
				var slopeL = lower[i] - lower[i - 2];
				var channelWidth = Math.Max(upper[i] - lower[i], 1e-8m);

				// If both slopes are nearly flat (<0.1% of price), reduce sensitivity
				if (Math.Abs(slopeU) < channelWidth * 0.001m && Math.Abs(slopeL) < channelWidth * 0.001m)
				{
					upper[i] = Math.Round((upper[i] * 0.5m + highs[i] * 0.5m), 4);
					lower[i] = Math.Round((lower[i] * 0.5m + lows[i] * 0.5m), 4);
				}
			}

			return (upper, lower);
		}


		// --- ATR (Average True Range) ---
		public static List<decimal> ATRList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			int n = Math.Min(highs.Count, Math.Min(lows.Count, closes.Count));
			var atr = Enumerable.Repeat(0m, n).ToList();
			if (n < period + 1) return atr;

			var tr = new decimal[n];
			for (int i = 1; i < n; i++)
			{
				decimal hl = highs[i] - lows[i];
				decimal hc = Math.Abs(highs[i] - closes[i - 1]);
				decimal lc = Math.Abs(lows[i] - closes[i - 1]);
				tr[i] = Math.Max(hl, Math.Max(hc, lc));
			}

			decimal firstAtr = tr.Skip(1).Take(period).Average();
			atr[period] = firstAtr;

			for (int i = period + 1; i < n; i++)
				atr[i] = (atr[i - 1] * (period - 1) + tr[i]) / period;

			for (int i = 1; i < n; i++)
				atr[i] = Math.Round((atr[i] * 0.85m + atr[i - 1] * 0.15m), 4);

			return atr;
		}

		// --- Pivot Points ---
		public static (decimal P, decimal R1, decimal S1) PivotPoints(decimal prevHigh, decimal prevLow, decimal prevClose)
		{
			if (prevHigh <= 0m && prevLow <= 0m && prevClose <= 0m)
				return (0m, 0m, 0m);

			var P = (prevHigh + prevLow + prevClose) / 3m;
			var R1 = 2m * P - prevLow;
			var S1 = 2m * P - prevHigh;
			return (P, R1, S1);
		}

		public static (decimal P, decimal R1, decimal S1) PivotPoints(IList<decimal> highs, IList<decimal> lows, IList<decimal> closes, int lookback = 1)
		{
			if (highs == null || lows == null || closes == null)
				return (0m, 0m, 0m);

			var n = Math.Min(highs.Count, Math.Min(lows.Count, closes.Count));
			if (n == 0) return (0m, 0m, 0m);

			lookback = Math.Max(1, Math.Min(lookback, n));
			int idx = n - lookback;

			var prevHigh = highs[idx];
			var prevLow = lows[idx];
			var prevClose = closes[idx];

			return PivotPoints(prevHigh, prevLow, prevClose);
		}

		// --- EMA (Exponential Moving Average) ---
		public static List<decimal> EMAList(List<decimal> values, int period)
		{
			int n = values?.Count ?? 0;
			var ema = Enumerable.Repeat(0m, n).ToList();
			if (n == 0) return ema;

			if (period <= 1 || n < period)
			{
				// Fallback: just clone the input or seed with average
				decimal seed = values.Average();
				for (int i = 0; i < n; i++) ema[i] = seed;
				return ema;
			}

			decimal k = 2m / (period + 1m);
			decimal prevEma = values.Take(period).Average();

			for (int i = 0; i < n; i++)
			{
				prevEma = (i < period)
					? prevEma
					: (values[i] * k) + (prevEma * (1m - k));

				ema[i] = Math.Round(prevEma, 4);
			}

			// Optional smoothing for stability
			for (int i = 1; i < n; i++)
				ema[i] = Math.Round((ema[i] * 0.9m + ema[i - 1] * 0.1m), 4);

			return ema;
		}

	}
}