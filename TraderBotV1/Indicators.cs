using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class Indicators
	{
		public static List<decimal> SMAList(List<decimal> vals, int period)
		{
			var outL = new List<decimal>();
			for (int i = 0; i < vals.Count; i++)
			{
				if (i + 1 < period) { outL.Add(0); continue; }
				outL.Add(vals.Skip(i + 1 - period).Take(period).Average());
			}
			return outL;
		}

		public static List<decimal> EMAList(List<decimal> vals, int period)
		{
			var outL = new List<decimal>();
			if (!vals.Any()) return outL;
			decimal k = 2m / (period + 1);
			decimal ema = vals.Take(period).Count() == period ? vals.Take(period).Average() : vals[0];
			for (int i = 0; i < vals.Count; i++)
			{
				if (i == 0 && vals.Count < period) { outL.Add(vals[0]); continue; }
				ema = (i == 0 && vals.Count >= period) ? ema : vals[i] * k + ema * (1 - k);
				outL.Add(ema);
			}
			return outL;
		}

		public static decimal RSI(List<decimal> closes, int period = 14)
		{
			if (closes.Count < period + 1) return 50m;
			decimal gains = 0, losses = 0;
			for (int i = closes.Count - period; i < closes.Count; i++)
			{
				var diff = closes[i] - closes[i - 1];
				if (diff > 0) gains += diff; else losses -= diff;
			}
			if (losses == 0) return 100m;
			var rs = gains / losses;
			return 100m - (100m / (1m + rs));
		}

		public static (List<decimal> macd, List<decimal> signal) MACDSeries(List<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
		{
			var emaFast = EMAList(closes, fast);
			var emaSlow = EMAList(closes, slow);
			var macd = new List<decimal>();
			for (int i = 0; i < closes.Count; i++) macd.Add(emaFast[i] - emaSlow[i]);
			var sig = EMAList(macd, signal);
			return (macd, sig);
		}

		public static (List<decimal> upper, List<decimal> middle, List<decimal> lower) BollingerBands(List<decimal> closes, int period = 20, decimal mult = 2m)
		{
			var middle = SMAList(closes, period);
			var upper = new List<decimal>();
			var lower = new List<decimal>();
			for (int i = 0; i < closes.Count; i++)
			{
				if (i + 1 < period) { upper.Add(0); lower.Add(0); continue; }
				var window = closes.Skip(i + 1 - period).Take(period).ToList();
				var mean = middle[i];
				var varr = window.Average(v => (v - mean) * (v - mean));
				var sd = (decimal)Math.Sqrt((double)varr);
				upper.Add(mean + mult * sd);
				lower.Add(mean - mult * sd);
			}
			return (upper, middle, lower);
		}

		public static List<decimal> ATRList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			var trs = new List<decimal>();
			for (int i = 1; i < closes.Count; i++)
			{
				var tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1])));
				trs.Add(tr);
			}
			return SMAList(trs, period);
		}
	}
}

