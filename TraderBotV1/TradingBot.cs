using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using TraderBotV1;

namespace TraderBotV1
{
	public static class Indicators
	{
		public static decimal RSI(List<decimal> closes, int period)
		{
			if (closes.Count < period + 1) return 50m;
			var gains = 0m;
			var losses = 0m;
			for (int i = closes.Count - period; i < closes.Count; i++)
			{
				var diff = closes[i] - closes[i - 1];
				if (diff > 0) gains += diff;
				else losses -= diff;
			}
			if (losses == 0) return 100m;
			var rs = gains / losses;
			return 100 - (100 / (1 + rs));
		}

		public static (decimal mid, decimal upper, decimal lower) Bollinger(List<decimal> closes, int period, decimal mult)
		{
			if (closes.Count < period) return (0, 0, 0);
			var subset = closes.Skip(closes.Count - period).ToList();
			var avg = subset.Average();
			var std = (decimal)Math.Sqrt(subset.Sum(v => Math.Pow((double)(v - avg), 2)) / period);
			return (avg, avg + mult * std, avg - mult * std);
		}

		public static (decimal macd, decimal signal) MACD(List<decimal> closes, int fast, int slow, int signalPeriod)
		{
			var emaFast = EMA(closes, fast);
			var emaSlow = EMA(closes, slow);
			var macd = emaFast - emaSlow;
			var signal = EMA(new List<decimal>(closes.Skip(closes.Count - signalPeriod).Append(macd).ToList()), signalPeriod);
			return (macd, signal);
		}

		public static decimal EMA(List<decimal> values, int period)
		{
			if (values.Count < period) return values.Last();
			decimal k = 2m / (period + 1);
			decimal ema = values.Take(period).Average();
			for (int i = period; i < values.Count; i++)
				ema = values[i] * k + ema * (1 - k);
			return ema;
		}

		public static decimal ATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
		{
			if (highs.Count < period + 1) return 0;
			var trs = new List<decimal>();
			for (int i = 1; i < highs.Count; i++)
			{
				var tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1])));
				trs.Add(tr);
			}
			return trs.Skip(trs.Count - period).Average();
		}

		public static List<decimal> ResampleToHigherTF(List<decimal> closes, int minuteFactor)
		{
			var res = new List<decimal>();
			for (int i = 0; i < closes.Count; i += minuteFactor)
				res.Add(closes.Skip(i).Take(minuteFactor).Average());
			return res;
		}
	}
}
