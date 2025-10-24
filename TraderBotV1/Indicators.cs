using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class Indicators
	{
		// --- Simple Moving Average ---
		public static List<decimal?> SMAList(List<decimal> vals, int period)
		{
			var output = new List<decimal?>(vals.Count);
			if (vals.Count == 0 || period <= 0) return output;

			decimal sum = 0;
			for (int i = 0; i < vals.Count; i++)
			{
				sum += vals[i];
				if (i >= period) sum -= vals[i - period];

				if (i + 1 < period)
					output.Add(null);
				else
				{
					var sma = sum / period;
					output.Add(Math.Round(sma, 4));
				}
			}
			return output;
		}

		// --- Exponential Moving Average (smoothed and stable) ---
		public static List<decimal> EMAList(List<decimal> vals, int period)
		{
			var ema = new List<decimal>(vals.Count);
			if (vals.Count < period || period <= 0)
				return Enumerable.Repeat(vals.Any() ? vals.Average() : 0m, vals.Count).ToList();

			decimal k = 2m / (period + 1);
			decimal prevEma = vals.Take(period).Average();

			for (int i = 0; i < vals.Count; i++)
			{
				if (i < period)
					ema.Add(prevEma);
				else
				{
					prevEma = vals[i] * k + prevEma * (1 - k);
					ema.Add(Math.Round(prevEma, 4));
				}
			}
			return ema;
		}

		// --- Single EMA (returns last value) ---
		public static decimal EMA(List<decimal> values, int period)
		{
			if (values.Count < period || period <= 0) return values.Any() ? values.Average() : 0m;

			decimal k = 2m / (period + 1);
			decimal ema = values.Take(period).Average();

			for (int i = period; i < values.Count; i++)
				ema = ((values[i] - ema) * k) + ema;

			return Math.Round(ema, 4);
		}

		// --- RSI (full series with Wilder's smoothing) ---
		public static List<decimal> RSIList(List<decimal> closes, int period = 14)
		{
			var result = new List<decimal>();
			if (closes.Count <= period) return result;

			decimal avgGain = 0, avgLoss = 0;
			for (int i = 1; i <= period; i++)
			{
				var diff = closes[i] - closes[i - 1];
				if (diff > 0) avgGain += diff;
				else avgLoss -= diff;
			}
			avgGain /= period; avgLoss /= period;

			decimal prevRSI = 100m - 100m / (1m + avgGain / (avgLoss == 0 ? 1e-8m : avgLoss));
			result.Add(prevRSI);

			// smoothing and adaptive filter
			const decimal hysteresis = 2.5m; // zone buffer to avoid flip-flops

			for (int i = period + 1; i < closes.Count; i++)
			{
				var diff = closes[i] - closes[i - 1];
				avgGain = (avgGain * (period - 1) + Math.Max(diff, 0)) / period;
				avgLoss = (avgLoss * (period - 1) + Math.Max(-diff, 0)) / period;

				var rs = avgLoss == 0 ? 1e6m : avgGain / avgLoss;
				var rsi = 100m - (100m / (1m + rs));

				// Trend filter via EMA(10)
				if (i > 10)
				{
					var emaShort = EMA(closes.Take(i + 1).ToList(), 10);
					var emaPrev = EMA(closes.Take(i).ToList(), 10);
					var trendUp = emaShort > emaPrev;
					if (!trendUp && rsi < 55m && rsi > 45m) rsi = prevRSI; // neutralize flat zone
				}

				// Apply hysteresis near 50
				if (Math.Abs(rsi - prevRSI) < hysteresis)
					rsi = (rsi + prevRSI) / 2m;

				prevRSI = rsi;
				result.Add(Math.Round(rsi, 2));
			}

			// smooth output with WMA
			for (int i = 2; i < result.Count; i++)
				result[i] = Math.Round((result[i] * 0.6m + result[i - 1] * 0.3m + result[i - 2] * 0.1m), 2);

			return result;
		}

		// --- MACD with Signal + Histogram (normalized) ---
		public static (List<decimal> macd, List<decimal> signal, List<decimal> hist) MACDSeries(
			List<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
		{
			if (closes.Count < slow + signal)
				return (new List<decimal>(), new List<decimal>(), new List<decimal>());

			var emaFast = EMAList(closes, fast);
			var emaSlow = EMAList(closes, slow);

			var macd = closes.Select((_, i) => emaFast[i] - emaSlow[i]).ToList();
			var validMacd = macd.Skip(slow - 1).ToList();
			var sig = EMAList(validMacd, signal);
			var paddedSig = Enumerable.Repeat(0m, slow - 1).Concat(sig).ToList();

			var hist = macd.Select((m, i) =>
				Math.Round((m - paddedSig[i]) / (Math.Abs(closes[i]) * 0.01m + 1e-8m), 4)
			).ToList();

			return (macd, paddedSig, hist);
		}

		// --- Improved Bollinger Bands with lag compensation ---
		public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
			BollingerBandsFast(List<decimal> closes, int period = 20, decimal mult = 2m)
		{
			var upper = new List<decimal?>();
			var middle = new List<decimal?>();
			var lower = new List<decimal?>();

			if (closes.Count < period)
				return (Enumerable.Repeat<decimal?>(null, closes.Count).ToList(),
						Enumerable.Repeat<decimal?>(null, closes.Count).ToList(),
						Enumerable.Repeat<decimal?>(null, closes.Count).ToList());

			const decimal lagAdjust = 0.9m;

			for (int i = 0; i < closes.Count; i++)
			{
				if (i + 1 < period)
				{
					upper.Add(null); middle.Add(null); lower.Add(null);
					continue;
				}

				var window = closes.GetRange(i + 1 - period, period);
				var mean = window.Average();
				var variance = window.Average(v => (v - mean) * (v - mean));
				var sd = (decimal)Math.Sqrt((double)variance);

				middle.Add(Math.Round(mean, 4));
				upper.Add(Math.Round(mean + mult * sd * lagAdjust, 4));
				lower.Add(Math.Round(mean - mult * sd * lagAdjust, 4));
			}

			return (upper, middle, lower);
		}

		// --- ATR (Wilder’s smoothing with ramp stabilization) ---
		public static List<decimal> ATRList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			if (closes.Count <= period) return new List<decimal>();

			var trs = new List<decimal>();
			for (int i = 1; i < closes.Count; i++)
			{
				var tr = Math.Max(highs[i] - lows[i],
					Math.Max(Math.Abs(highs[i] - closes[i - 1]),
							 Math.Abs(lows[i] - closes[i - 1])));
				trs.Add(tr);
			}

			var atr = new List<decimal>();
			decimal firstATR = trs.Take(period).Average();
			atr.Add(firstATR);

			for (int i = period; i < trs.Count; i++)
			{
				decimal rawAtr = (atr.Last() * (period - 1) + trs[i]) / period;
				if (i < period * 2) rawAtr *= 1.05m; // early expansion ramp
				atr.Add(Math.Round(rawAtr, 4));
			}

			// Align length with closes
			var padCount = closes.Count - atr.Count;
			atr.InsertRange(0, Enumerable.Repeat(0m, padCount));

			return atr;
		}
	}
}
