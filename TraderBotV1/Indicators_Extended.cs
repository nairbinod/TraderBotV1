using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class IndicatorsExtended
	{
		// --- Standard StochRSI ---
		public static (List<decimal> k, List<decimal> d) StochRSIList(
			List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
		{
			int n = closes?.Count ?? 0;
			var kList = new List<decimal>(new decimal[n]);
			var dList = new List<decimal>(new decimal[n]);

			if (n < rsiPeriod + stochPeriod + Math.Max(smoothK, smoothD))
				return (kList, dList);

			var rsiVals = Indicators.RSIList(closes, rsiPeriod);
			if (rsiVals.Count < stochPeriod + 1)
				return (kList, dList);

			int validLen = Math.Min(n, rsiVals.Count);
			var kRaw = new decimal[validLen];

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
				kList[i] = Math.Round(kRaw.Skip(start).Take(i - start + 1).Average(), 4);
			}

			// Smooth D
			for (int i = 0; i < validLen; i++)
			{
				int start = Math.Max(0, i - smoothD + 1);
				dList[i] = Math.Round(kList.Skip(start).Take(i - start + 1).Average(), 4);
			}

			return (kList, dList);
		}

		// --- Standard ADX ---
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

				diPlusList[i] = Math.Round(diPlus, 2);
				diMinusList[i] = Math.Round(diMinus, 2);

				decimal sum = diPlus + diMinus;
				dx[i] = (sum == 0) ? 0m : 100m * Math.Abs(diPlus - diMinus) / sum;
			}

			// Step 3: Smooth ADX
			int adxStart = Math.Min(period * 2 - 1, n - 1);
			decimal firstAdx = dx.Skip(period).Take(period).DefaultIfEmpty(0m).Average();
			adx[adxStart] = firstAdx;

			for (int i = adxStart + 1; i < n; i++)
				adx[i] = Math.Round((adx[i - 1] * (period - 1) + dx[i]) / period, 2);

			return (adx, diPlusList, diMinusList);
		}

		// --- Standard CCI ---
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

			return cci;
		}

		// --- Standard Donchian Channel ---
		public static (List<decimal> upper, List<decimal> lower) DonchianChannel(
			List<decimal> highs, List<decimal> lows, int period = 20)
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

				upper[i] = Math.Round(highSlice.Max(), 4);
				lower[i] = Math.Round(lowSlice.Min(), 4);
			}

			return (upper, lower);
		}

		// --- Standard ATR ---
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
				atr[i] = Math.Round((atr[i - 1] * (period - 1) + tr[i]) / period, 4);

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

		// --- Standard EMA (alternative implementation) ---
		public static List<decimal> EMAList(List<decimal> values, int period)
		{
			int n = values?.Count ?? 0;
			var ema = Enumerable.Repeat(0m, n).ToList();
			if (n == 0) return ema;

			if (period <= 1 || n < period)
			{
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

			return ema;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// EXTENDED SIGNAL VALIDATORS
	// ═══════════════════════════════════════════════════════════════
	public static class ExtendedSignalValidator
	{
		// --- StochRSI Signal Validation ---
		public static SignalValidation ValidateStochRSI(List<decimal> stochK, List<decimal> stochD,
			List<decimal> rsi, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || stochK.Count <= idx || stochD.Count <= idx)
				return validation.Fail("Insufficient StochRSI data");

			bool isBuy = direction == "Buy";
			decimal kNow = stochK[idx];
			decimal kPrev = stochK[idx - 1];
			decimal dNow = stochD[idx];

			// Avoid neutral zone
			if (kNow > 0.4m && kNow < 0.6m)
				return validation.Fail("StochRSI in neutral zone");

			if (isBuy)
			{
				// Must be recovering from oversold
				bool wasOversold = stochK.Skip(Math.Max(0, idx - 5)).Take(5).Any(k => k < 0.2m);
				if (!wasOversold)
					return validation.Fail("No oversold condition");

				// Crossover: K crosses above D
				bool crossover = kNow > dNow && kPrev <= stochD[idx - 1];
				if (!crossover)
					return validation.Fail("No bullish crossover");

				// RSI confirmation (if available)
				bool rsiConfirm = rsi.Count > idx && rsi[idx] > 40m && rsi[idx] < 70m;

				validation.IsValid = true;
				validation.Confidence = rsiConfirm ? 0.8m : 0.65m;
				validation.Reason = $"StochRSI buy (K={kNow:F2}, D={dNow:F2})";
			}
			else // Sell
			{
				bool wasOverbought = stochK.Skip(Math.Max(0, idx - 5)).Take(5).Any(k => k > 0.8m);
				if (!wasOverbought)
					return validation.Fail("No overbought condition");

				bool crossover = kNow < dNow && kPrev >= stochD[idx - 1];
				if (!crossover)
					return validation.Fail("No bearish crossover");

				bool rsiConfirm = rsi.Count > idx && rsi[idx] < 60m && rsi[idx] > 30m;

				validation.IsValid = true;
				validation.Confidence = rsiConfirm ? 0.8m : 0.65m;
				validation.Reason = $"StochRSI sell (K={kNow:F2}, D={dNow:F2})";
			}

			return validation;
		}

		// --- ADX Trend Validation ---
		public static SignalValidation ValidateADX(List<decimal> adx, List<decimal> diPlus,
			List<decimal> diMinus, int idx, string direction, decimal threshold = 25m)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 3 || adx.Count <= idx)
				return validation.Fail("Insufficient ADX data");

			decimal adxNow = adx[idx];
			decimal adxPrev = adx[idx - 1];
			decimal diP = diPlus[idx];
			decimal diM = diMinus[idx];

			// Must have trend strength
			if (adxNow < threshold)
				return validation.Fail($"ADX too weak: {adxNow:F1} < {threshold}");

			// ADX should be rising (strengthening trend)
			if (adxNow <= adxPrev)
				return validation.Fail("ADX not rising");

			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// DI+ must dominate DI-
				if (diP <= diM + 5m)
					return validation.Fail($"DI+ not dominant: {diP:F1} vs {diM:F1}");

				validation.IsValid = true;
				validation.Confidence = Math.Min((adxNow - threshold) / 30m + 0.6m, 1m);
				validation.Reason = $"Strong uptrend (ADX={adxNow:F1}, DI+={diP:F1})";
			}
			else // Sell
			{
				if (diM <= diP + 5m)
					return validation.Fail($"DI- not dominant: {diM:F1} vs {diP:F1}");

				validation.IsValid = true;
				validation.Confidence = Math.Min((adxNow - threshold) / 30m + 0.6m, 1m);
				validation.Reason = $"Strong downtrend (ADX={adxNow:F1}, DI-={diM:F1})";
			}

			return validation;
		}

		// --- CCI Reversal Validation ---
		public static SignalValidation ValidateCCI(List<decimal> cci, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 3 || cci.Count <= idx)
				return validation.Fail("Insufficient CCI data");

			decimal cciNow = cci[idx];
			decimal cciPrev = cci[idx - 1];
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// Must cross above -100 (oversold recovery)
				if (!(cciPrev <= -100m && cciNow > -100m))
					return validation.Fail("No CCI oversold recovery");

				// Momentum confirmation
				bool momentum = cciNow > cciPrev && cci[idx - 1] > cci[idx - 2];

				validation.IsValid = true;
				validation.Confidence = momentum ? 0.75m : 0.6m;
				validation.Reason = $"CCI buy signal ({cciPrev:F0}→{cciNow:F0})";
			}
			else // Sell
			{
				if (!(cciPrev >= 100m && cciNow < 100m))
					return validation.Fail("No CCI overbought reversal");

				bool momentum = cciNow < cciPrev && cci[idx - 1] < cci[idx - 2];

				validation.IsValid = true;
				validation.Confidence = momentum ? 0.75m : 0.6m;
				validation.Reason = $"CCI sell signal ({cciPrev:F0}→{cciNow:F0})";
			}

			return validation;
		}

		// --- Donchian Breakout Validation ---
		public static SignalValidation ValidateDonchianBreakout(List<decimal> prices, List<decimal> highs,
			List<decimal> lows, List<decimal> upper, List<decimal> lower, List<decimal> atr, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || upper.Count <= idx || lower.Count <= idx)
				return validation.Fail("Insufficient Donchian data");

			decimal price = prices[idx];
			decimal u = upper[idx];
			decimal l = lower[idx];
			bool isBuy = direction == "Buy";

			// Volatility check
			decimal vol = atr.Count > idx && price > 0 ? atr[idx] / price : 0m;
			if (vol < 0.005m)
				return validation.Fail($"Insufficient volatility: {vol:P2}");

			if (isBuy)
			{
				// Must break above upper band
				if (price <= u)
					return validation.Fail("No breakout above upper band");

				// Confirmation: previous bar should be near or below the band
				if (prices[idx - 1] > u)
					return validation.Fail("No clear breakout (already above)");

				// Volume/momentum check (price rising)
				bool momentum = prices[idx] > prices[idx - 2];
				if (!momentum)
					return validation.Fail("Weak momentum");

				decimal breakoutStrength = (price - u) / Math.Max(price * 0.02m, 1e-8m);
				validation.IsValid = true;
				validation.Confidence = Math.Min(breakoutStrength * 2m + 0.5m, 1m);
				validation.Reason = $"Donchian breakout ↑ (${price:F2} > ${u:F2})";
			}
			else // Sell
			{
				if (price >= l)
					return validation.Fail("No breakdown below lower band");

				if (prices[idx - 1] < l)
					return validation.Fail("No clear breakdown (already below)");

				bool momentum = prices[idx] < prices[idx - 2];
				if (!momentum)
					return validation.Fail("Weak momentum");

				decimal breakdownStrength = (l - price) / Math.Max(price * 0.02m, 1e-8m);
				validation.IsValid = true;
				validation.Confidence = Math.Min(breakdownStrength * 2m + 0.5m, 1m);
				validation.Reason = $"Donchian breakdown ↓ (${price:F2} < ${l:F2})";
			}

			return validation;
		}

		// --- Volume Spike Validation ---
		public static SignalValidation ValidateVolumeSpike(List<decimal> volumes, List<decimal> prices,
			int idx, decimal spikeMultiple = 1.5m)
		{
			var validation = new SignalValidation { IsValid = false };

			if (volumes == null || volumes.Count <= idx || idx < 20)
				return validation.Fail("Insufficient volume data");

			decimal currentVol = volumes[idx];
			var recentVols = volumes.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
			decimal avgVol = recentVols.Average();

			if (avgVol <= 0)
				return validation.Fail("Invalid volume baseline");

			decimal volMultiple = currentVol / avgVol;
			if (volMultiple < spikeMultiple)
				return validation.Fail($"No volume spike: {volMultiple:F2}x");

			// Direction check
			bool upBar = prices[idx] > prices[idx - 1];
			string direction = upBar ? "Buy" : "Sell";

			validation.IsValid = true;
			validation.Confidence = Math.Min((volMultiple - spikeMultiple) / spikeMultiple + 0.6m, 1m);
			validation.Reason = $"Volume spike {volMultiple:F2}x on {(upBar ? "up" : "down")} bar";

			return validation;
		}
	}
}