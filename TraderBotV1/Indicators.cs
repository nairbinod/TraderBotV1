using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	// ═══════════════════════════════════════════════════════════════
	// CLEAN INDICATOR CALCULATIONS (No Signal Filtering)
	// ═══════════════════════════════════════════════════════════════
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

		// --- Standard EMA (List) ---
		public static List<decimal> EMAList(List<decimal> vals, int period)
		{
			var ema = new List<decimal>(vals.Count);
			if (vals.Count < period || period <= 0)
				return Enumerable.Repeat(vals.Any() ? vals.Average() : 0m, vals.Count).ToList();

			decimal k = 2m / (period + 1);
			decimal currentEma = vals.Take(period).Average();

			for (int i = 0; i < vals.Count; i++)
			{
				if (i < period)
				{
					ema.Add(currentEma);
				}
				else
				{
					currentEma = vals[i] * k + currentEma * (1 - k);
					ema.Add(Math.Round(currentEma, 4));
				}
			}

			return ema;
		}

		// --- Standard EMA (Single Value) ---
		public static decimal EMA(List<decimal> values, int period)
		{
			if (values == null || values.Count < period || period <= 0)
				return values?.Any() == true ? values.Average() : 0m;

			decimal k = 2m / (period + 1);
			decimal ema = values.Take(period).Average();

			for (int i = period; i < values.Count; i++)
			{
				ema = values[i] * k + ema * (1 - k);
			}

			return Math.Round(ema, 4);
		}

		// --- Standard RSI (Wilder's Method) ---
		public static List<decimal> RSIList(List<decimal> closes, int period = 14)
		{
			var result = new List<decimal>();
			if (closes == null || closes.Count <= period)
				return result;

			decimal avgGain = 0, avgLoss = 0;
			for (int i = 1; i <= period; i++)
			{
				var diff = closes[i] - closes[i - 1];
				if (diff > 0) avgGain += diff;
				else avgLoss -= diff;
			}

			avgGain /= period;
			avgLoss /= period;

			decimal rs = avgLoss == 0 ? 100m : avgGain / Math.Max(avgLoss, 1e-10m);
			decimal rsi = 100m - (100m / (1m + rs));
			result.Add(Math.Round(rsi, 2));

			for (int i = period + 1; i < closes.Count; i++)
			{
				var diff = closes[i] - closes[i - 1];
				avgGain = (avgGain * (period - 1) + Math.Max(diff, 0)) / period;
				avgLoss = (avgLoss * (period - 1) + Math.Max(-diff, 0)) / period;

				rs = avgLoss == 0 ? 100m : avgGain / Math.Max(avgLoss, 1e-10m);
				rsi = 100m - (100m / (1m + rs));

				result.Add(Math.Round(rsi, 2));
			}

			return result;
		}

		// --- Standard MACD ---
		public static (List<decimal> macd, List<decimal> signal, List<decimal> hist) MACDSeries(
			List<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
		{
			int n = closes.Count;
			if (n < slow + signal)
				return (new List<decimal>(), new List<decimal>(), new List<decimal>());

			var emaFast = EMAList(closes, fast);
			var emaSlow = EMAList(closes, slow);
			var macd = closes.Select((_, i) => emaFast[i] - emaSlow[i]).ToList();

			var validMacd = macd.Skip(slow - 1).ToList();
			var sig = EMAList(validMacd, signal);
			var paddedSig = Enumerable.Repeat(0m, slow - 1).Concat(sig).ToList();

			var hist = macd.Select((m, i) => Math.Round(m - paddedSig[i], 4)).ToList();

			return (macd, paddedSig, hist);
		}

		// --- Bollinger Bands ---
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
				upper.Add(Math.Round(mean + mult * sd, 4));
				lower.Add(Math.Round(mean - mult * sd, 4));
			}

			return (upper, middle, lower);
		}

		// --- ATR (Average True Range) ---
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
			decimal currentATR = trs.Take(period).Average();
			atr.Add(currentATR);

			for (int i = period; i < trs.Count; i++)
			{
				currentATR = (currentATR * (period - 1) + trs[i]) / period;
				atr.Add(Math.Round(currentATR, 4));
			}

			var padCount = closes.Count - atr.Count;
			atr.InsertRange(0, Enumerable.Repeat(0m, padCount));

			return atr;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// SIGNAL VALIDATION FRAMEWORK (Handles False Signals)
	// ═══════════════════════════════════════════════════════════════
	public static class SignalValidator
	{
		// --- Market Context Analysis ---
		public static MarketContext AnalyzeMarketContext(List<decimal> prices, List<decimal> highs,
			List<decimal> lows, int idx)
		{
			if (idx < 50) return new MarketContext();

			var recentPrices = prices.Skip(Math.Max(0, idx - 20)).Take(20).ToList();
			var mediumPrices = prices.Skip(Math.Max(0, idx - 50)).Take(50).ToList();

			decimal recentVol = CalculateVolatility(recentPrices);
			decimal mediumVol = CalculateVolatility(mediumPrices);

			decimal recentRange = (recentPrices.Max() - recentPrices.Min()) / Math.Max(recentPrices.Average(), 1e-8m);

			var ema20 = Indicators.EMA(prices.Take(idx + 1).ToList(), 20);
			var ema50 = Indicators.EMA(prices.Take(idx + 1).ToList(), 50);
			var ema200 = idx >= 200 ? Indicators.EMA(prices.Take(idx + 1).ToList(), 200) : ema50;

			return new MarketContext
			{
				RecentVolatility = recentVol,
				MediumVolatility = mediumVol,
				VolatilityRatio = mediumVol > 0 ? recentVol / mediumVol : 1m,
				RecentRange = recentRange,
				IsUptrend = ema20 > ema50 && ema50 > ema200,
				IsDowntrend = ema20 < ema50 && ema50 < ema200,
				IsSideways = Math.Abs(ema20 - ema50) / Math.Max(ema50, 1e-8m) < 0.005m,
				TrendStrength = Math.Abs(ema20 - ema50) / Math.Max(ema50, 1e-8m)
			};
		}

		// --- EMA Crossover Validation ---
		public static SignalValidation ValidateEMACrossover(List<decimal> prices, List<decimal> fastEma,
			List<decimal> slowEma, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || fastEma.Count <= idx || slowEma.Count <= idx)
				return validation.Fail("Insufficient data");

			bool isBuy = direction == "Buy";
			bool crossover = isBuy
				? (fastEma[idx] > slowEma[idx] && fastEma[idx - 1] <= slowEma[idx - 1])
				: (fastEma[idx] < slowEma[idx] && fastEma[idx - 1] >= slowEma[idx - 1]);

			if (!crossover)
				return validation.Fail("No crossover detected");

			var context = AnalyzeMarketContext(prices, prices, prices, idx);

			// 1. Volatility check
			if (context.VolatilityRatio > 2.5m)
				return validation.Fail("Extreme volatility spike");

			// 2. Minimum separation
			decimal separation = Math.Abs(fastEma[idx] - slowEma[idx]) / slowEma[idx];
			if (separation < 0.002m)
				return validation.Fail($"Gap too small: {separation:P2}");

			// 3. Momentum confirmation (3 bars)
			bool momentum = isBuy
				? (fastEma[idx] > fastEma[idx - 3])
				: (fastEma[idx] < fastEma[idx - 3]);

			if (!momentum)
				return validation.Fail("Weak momentum");

			// 4. Price confirmation
			bool priceConfirm = isBuy
				? (prices[idx] > prices[idx - 2])
				: (prices[idx] < prices[idx - 2]);

			if (!priceConfirm)
				return validation.Fail("Price divergence");

			// 5. Trend alignment (optional but recommended)
			bool trendAligned = isBuy ? !context.IsDowntrend : !context.IsUptrend;

			validation.IsValid = true;
			validation.Confidence = CalculateConfidence(separation, momentum, priceConfirm, trendAligned);
			validation.Reason = $"Valid crossover with {validation.Confidence:P0} confidence";

			return validation;
		}

		// --- RSI Signal Validation ---
		public static SignalValidation ValidateRSI(List<decimal> rsi, List<decimal> prices, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 10 || rsi.Count <= idx)
				return validation.Fail("Insufficient RSI data");

			bool isBuy = direction == "Buy";
			decimal rsiNow = rsi[idx];

			if (isBuy)
			{
				// Must have been oversold recently
				bool wasOversold = rsi.Skip(Math.Max(0, idx - 10)).Take(10).Any(r => r < 30m);
				if (!wasOversold)
					return validation.Fail("No oversold condition");

				// Now recovering
				if (rsiNow < 30m || rsiNow > 70m)
					return validation.Fail($"RSI out of range: {rsiNow:F1}");

				// Rising momentum
				if (!(rsi[idx] > rsi[idx - 1] && rsi[idx - 1] > rsi[idx - 2]))
					return validation.Fail("RSI not rising");

				// Check for bullish divergence
				bool bullishDiv = CheckBullishDivergence(prices, rsi, idx, 15);

				validation.IsValid = true;
				validation.Confidence = bullishDiv ? 0.9m : 0.7m;
				validation.Reason = bullishDiv
					? $"RSI buy with bullish divergence (RSI={rsiNow:F1})"
					: $"RSI buy signal (RSI={rsiNow:F1})";
			}
			else // Sell
			{
				bool wasOverbought = rsi.Skip(Math.Max(0, idx - 10)).Take(10).Any(r => r > 70m);
				if (!wasOverbought)
					return validation.Fail("No overbought condition");

				if (rsiNow > 70m || rsiNow < 30m)
					return validation.Fail($"RSI out of range: {rsiNow:F1}");

				if (!(rsi[idx] < rsi[idx - 1] && rsi[idx - 1] < rsi[idx - 2]))
					return validation.Fail("RSI not falling");

				bool bearishDiv = CheckBearishDivergence(prices, rsi, idx, 15);

				validation.IsValid = true;
				validation.Confidence = bearishDiv ? 0.9m : 0.7m;
				validation.Reason = bearishDiv
					? $"RSI sell with bearish divergence (RSI={rsiNow:F1})"
					: $"RSI sell signal (RSI={rsiNow:F1})";
			}

			return validation;
		}

		// --- MACD Signal Validation ---
		public static SignalValidation ValidateMACD(List<decimal> macdHist, List<decimal> prices,
			List<decimal> macdLine, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || macdHist.Count <= idx)
				return validation.Fail("Insufficient MACD data");

			bool isBuy = direction == "Buy";

			// Crossover detection
			bool crossover = isBuy
				? (macdHist[idx] > 0 && macdHist[idx - 1] <= 0)
				: (macdHist[idx] < 0 && macdHist[idx - 1] >= 0);

			if (!crossover)
				return validation.Fail("No MACD crossover");

			// Histogram strength
			decimal pricePercent = Math.Abs(prices[idx]) * 0.0015m; // 0.15% of price
			if (Math.Abs(macdHist[idx]) < pricePercent)
				return validation.Fail($"Histogram too weak: {macdHist[idx]:F4}");

			// Histogram momentum
			bool histMomentum = isBuy
				? (macdHist[idx] > macdHist[idx - 1])
				: (macdHist[idx] < macdHist[idx - 1]);

			if (!histMomentum)
				return validation.Fail("Weak histogram momentum");

			// Anti-whipsaw: no recent opposite cross
			bool recentWhipsaw = idx >= 4 && (isBuy
				? (macdHist[idx - 3] < 0 && macdHist[idx - 4] >= 0)
				: (macdHist[idx - 3] > 0 && macdHist[idx - 4] <= 0));

			if (recentWhipsaw)
				return validation.Fail("Recent whipsaw detected");

			// MACD line trend
			bool macdTrend = isBuy
				? (macdLine[idx] > macdLine[idx - 3])
				: (macdLine[idx] < macdLine[idx - 3]);

			validation.IsValid = true;
			validation.Confidence = macdTrend ? 0.85m : 0.65m;
			validation.Reason = $"Valid MACD {direction} (hist={macdHist[idx]:F4})";

			return validation;
		}

		// --- Multi-Indicator Confirmation ---
		public static bool RequiresMultiIndicatorConfirmation(MarketContext context)
		{
			// Require extra confirmation in choppy/sideways markets
			return context.IsSideways || context.VolatilityRatio > 2m || context.RecentRange < 0.01m;
		}

		// --- Helper: Volatility Calculation ---
		private static decimal CalculateVolatility(List<decimal> prices)
		{
			if (prices.Count < 2) return 0m;

			decimal avg = prices.Average();
			decimal sumSquares = prices.Sum(p => (p - avg) * (p - avg));
			return (decimal)Math.Sqrt((double)(sumSquares / prices.Count));
		}

		// --- Helper: Bullish Divergence ---
		private static bool CheckBullishDivergence(List<decimal> prices, List<decimal> indicator,
			int idx, int lookback)
		{
			if (idx < lookback + 2) return false;

			var recentPrices = prices.Skip(idx - lookback).Take(lookback + 1).ToList();
			var recentIndicator = indicator.Skip(idx - lookback).Take(lookback + 1).ToList();

			int priceLowIdx = recentPrices.IndexOf(recentPrices.Min());
			int priceSecondLowIdx = -1;
			decimal secondLow = decimal.MaxValue;

			for (int i = 0; i < recentPrices.Count; i++)
			{
				if (i != priceLowIdx && recentPrices[i] < secondLow)
				{
					secondLow = recentPrices[i];
					priceSecondLowIdx = i;
				}
			}

			if (priceSecondLowIdx == -1) return false;

			bool priceLowerLow = recentPrices[priceLowIdx] < secondLow;
			bool indicatorHigherLow = recentIndicator[priceLowIdx] > recentIndicator[priceSecondLowIdx];

			return priceLowerLow && indicatorHigherLow;
		}

		// --- Helper: Bearish Divergence ---
		private static bool CheckBearishDivergence(List<decimal> prices, List<decimal> indicator,
			int idx, int lookback)
		{
			if (idx < lookback + 2) return false;

			var recentPrices = prices.Skip(idx - lookback).Take(lookback + 1).ToList();
			var recentIndicator = indicator.Skip(idx - lookback).Take(lookback + 1).ToList();

			int priceHighIdx = recentPrices.IndexOf(recentPrices.Max());
			int priceSecondHighIdx = -1;
			decimal secondHigh = decimal.MinValue;

			for (int i = 0; i < recentPrices.Count; i++)
			{
				if (i != priceHighIdx && recentPrices[i] > secondHigh)
				{
					secondHigh = recentPrices[i];
					priceSecondHighIdx = i;
				}
			}

			if (priceSecondHighIdx == -1) return false;

			bool priceHigherHigh = recentPrices[priceHighIdx] > secondHigh;
			bool indicatorLowerHigh = recentIndicator[priceHighIdx] < recentIndicator[priceSecondHighIdx];

			return priceHigherHigh && indicatorLowerHigh;
		}

		// --- Helper: Calculate Confidence ---
		private static decimal CalculateConfidence(decimal separation, bool momentum,
			bool priceConfirm, bool trendAligned)
		{
			decimal conf = 0.3m; // Base
			conf += Math.Min(separation * 50m, 0.3m); // Separation weight
			if (momentum) conf += 0.2m;
			if (priceConfirm) conf += 0.15m;
			if (trendAligned) conf += 0.05m;
			return Math.Min(conf, 1m);
		}
	}

	// --- Support Classes ---
	public class MarketContext
	{
		public decimal RecentVolatility { get; set; }
		public decimal MediumVolatility { get; set; }
		public decimal VolatilityRatio { get; set; }
		public decimal RecentRange { get; set; }
		public bool IsUptrend { get; set; }
		public bool IsDowntrend { get; set; }
		public bool IsSideways { get; set; }
		public decimal TrendStrength { get; set; }
	}

	public class SignalValidation
	{
		public bool IsValid { get; set; }
		public decimal Confidence { get; set; }
		public string Reason { get; set; } = "";

		public SignalValidation Fail(string reason)
		{
			IsValid = false;
			Confidence = 0m;
			Reason = reason;
			return this;
		}
	}
}