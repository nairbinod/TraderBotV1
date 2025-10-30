using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Additional high-quality trading strategies to improve signal accuracy
	/// </summary>
	public static class StrategiesAdvanced
	{
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));
		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);

		// ═══════════════════════════════════════════════════════════════
		// 1) VWAP (Volume Weighted Average Price) Strategy
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal VWAPStrategy(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (closes.Count < 20 || volumes == null || volumes.Count != closes.Count)
				return Hold("Insufficient data for VWAP");

			int lookback = Math.Min(20, closes.Count);
			decimal vwap = CalculateVWAP(closes, highs, lows, volumes, lookback);

			if (vwap == 0) return Hold("Invalid VWAP");

			decimal price = closes.Last();
			decimal deviation = (price - vwap) / vwap;

			// Calculate volume trend
			var recentVol = volumes.Skip(volumes.Count - 5).Average();
			var avgVol = volumes.Skip(Math.Max(0, volumes.Count - 20)).Take(20).Average();
			decimal volRatio = avgVol > 0 ? recentVol / avgVol : 1m;

			// Buy when price crosses above VWAP with volume
			if (closes[closes.Count - 2] <= vwap && price > vwap && volRatio > 1.2m)
			{
				decimal strength = Clamp01(Math.Abs(deviation) * 10m + (volRatio - 1m) * 0.3m);
				return new("Buy", strength, $"Price crossed above VWAP (${vwap:F2}) with {volRatio:F1}x volume");
			}

			// Sell when price crosses below VWAP with volume
			if (closes[closes.Count - 2] >= vwap && price < vwap && volRatio > 1.2m)
			{
				decimal strength = Clamp01(Math.Abs(deviation) * 10m + (volRatio - 1m) * 0.3m);
				return new("Sell", strength, $"Price crossed below VWAP (${vwap:F2}) with {volRatio:F1}x volume");
			}

			return Hold($"Price near VWAP (${vwap:F2})");
		}

		private static decimal CalculateVWAP(List<decimal> closes, List<decimal> highs,
			List<decimal> lows, List<decimal> volumes, int lookback)
		{
			decimal cumVolPrice = 0m;
			decimal cumVol = 0m;

			for (int i = Math.Max(0, closes.Count - lookback); i < closes.Count; i++)
			{
				decimal typical = (highs[i] + lows[i] + closes[i]) / 3m;
				cumVolPrice += typical * volumes[i];
				cumVol += volumes[i];
			}

			return cumVol > 0 ? cumVolPrice / cumVol : 0m;
		}

		// ═══════════════════════════════════════════════════════════════
		// 2) Ichimoku Cloud Strategy (Simplified)
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal IchimokuCloud(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			if (closes.Count < 52) return Hold("Insufficient data for Ichimoku");

			var (tenkan, kijun, senkouA, senkouB) = CalculateIchimoku(highs, lows, closes);

			decimal price = closes.Last();
			decimal cloudTop = Math.Max(senkouA, senkouB);
			decimal cloudBottom = Math.Min(senkouA, senkouB);

			// Determine trend
			bool bullishCloud = senkouA > senkouB;
			bool priceAboveCloud = price > cloudTop;
			bool priceBelowCloud = price < cloudBottom;
			bool tenkanAboveKijun = tenkan > kijun;

			// Strong buy: Price above bullish cloud, Tenkan above Kijun
			if (priceAboveCloud && bullishCloud && tenkanAboveKijun)
			{
				decimal strength = Clamp01((price - cloudTop) / price * 20m + 0.5m);
				return new("Buy", strength, $"Bullish Ichimoku: Price above cloud, TK cross bullish");
			}

			// Strong sell: Price below bearish cloud, Tenkan below Kijun
			if (priceBelowCloud && !bullishCloud && !tenkanAboveKijun)
			{
				decimal strength = Clamp01((cloudBottom - price) / price * 20m + 0.5m);
				return new("Sell", strength, $"Bearish Ichimoku: Price below cloud, TK cross bearish");
			}

			// Breakout buy: Price crossing above cloud
			if (closes[closes.Count - 2] <= cloudTop && price > cloudTop && bullishCloud)
			{
				return new("Buy", 0.75m, "Ichimoku cloud breakout bullish");
			}

			// Breakdown sell: Price crossing below cloud
			if (closes[closes.Count - 2] >= cloudBottom && price < cloudBottom && !bullishCloud)
			{
				return new("Sell", 0.75m, "Ichimoku cloud breakdown bearish");
			}

			return Hold("Ichimoku neutral (inside cloud or mixed signals)");
		}

		private static (decimal tenkan, decimal kijun, decimal senkouA, decimal senkouB)
			CalculateIchimoku(List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			int idx = closes.Count - 1;

			// Tenkan-sen (Conversion Line): (9-period high + 9-period low)/2
			decimal tenkan = (highs.Skip(idx - 8).Take(9).Max() + lows.Skip(idx - 8).Take(9).Min()) / 2m;

			// Kijun-sen (Base Line): (26-period high + 26-period low)/2
			decimal kijun = (highs.Skip(idx - 25).Take(26).Max() + lows.Skip(idx - 25).Take(26).Min()) / 2m;

			// Senkou Span A: (Tenkan + Kijun)/2 projected 26 periods ahead
			decimal senkouA = (tenkan + kijun) / 2m;

			// Senkou Span B: (52-period high + 52-period low)/2 projected 26 periods ahead
			decimal senkouB = (highs.Skip(idx - 51).Take(52).Max() + lows.Skip(idx - 51).Take(52).Min()) / 2m;

			return (tenkan, kijun, senkouA, senkouB);
		}

		// ═══════════════════════════════════════════════════════════════
		// 3) Price Action: Higher Highs / Lower Lows
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal PriceActionTrend(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			if (closes.Count < 20) return Hold("Insufficient data");

			var swingPoints = FindSwingPoints(highs, lows, closes, lookback: 15);

			if (swingPoints.Count < 4) return Hold("Not enough swing points");

			// Check for higher highs and higher lows (uptrend)
			bool higherHighs = swingPoints.Where(p => p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value > a.value)
				.All(x => x);

			bool higherLows = swingPoints.Where(p => !p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => !p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value > a.value)
				.All(x => x);

			// Check for lower highs and lower lows (downtrend)
			bool lowerHighs = swingPoints.Where(p => p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value < a.value)
				.All(x => x);

			bool lowerLows = swingPoints.Where(p => !p.isHigh)
				.OrderBy(p => p.index)
				.Zip(swingPoints.Where(p => !p.isHigh).OrderBy(p => p.index).Skip(1),
					(a, b) => b.value < a.value)
				.All(x => x);

			// Confirmed uptrend
			if (higherHighs && higherLows)
			{
				decimal strength = 0.8m;
				return new("Buy", strength, "Strong uptrend: Higher highs & higher lows confirmed");
			}

			// Confirmed downtrend
			if (lowerHighs && lowerLows)
			{
				decimal strength = 0.8m;
				return new("Sell", strength, "Strong downtrend: Lower highs & lower lows confirmed");
			}

			return Hold("No clear price action trend");
		}

		private static List<(int index, decimal value, bool isHigh)> FindSwingPoints(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, int lookback)
		{
			var swings = new List<(int index, decimal value, bool isHigh)>();

			for (int i = lookback; i < closes.Count - lookback; i++)
			{
				// Check for swing high
				bool isSwingHigh = true;
				for (int j = i - lookback; j < i + lookback; j++)
				{
					if (j != i && highs[j] >= highs[i])
					{
						isSwingHigh = false;
						break;
					}
				}
				if (isSwingHigh) swings.Add((i, highs[i], true));

				// Check for swing low
				bool isSwingLow = true;
				for (int j = i - lookback; j < i + lookback; j++)
				{
					if (j != i && lows[j] <= lows[i])
					{
						isSwingLow = false;
						break;
					}
				}
				if (isSwingLow) swings.Add((i, lows[i], false));
			}

			return swings.OrderBy(s => s.index).ToList();
		}

		// ═══════════════════════════════════════════════════════════════
		// 4) Squeeze Momentum (Bollinger Bands + Keltner Channels)
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal SqueezeMomentum(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			int bbPeriod = 20,
			int kcPeriod = 20,
			decimal bbMult = 2m,
			decimal kcMult = 1.5m)
		{
			if (closes.Count < Math.Max(bbPeriod, kcPeriod) + 10)
				return Hold("Insufficient data for Squeeze");

			var atr = Indicators.ATRList(highs, lows, closes, kcPeriod);
			var (bbUpper, bbMiddle, bbLower) = Indicators.BollingerBandsFast(closes, bbPeriod, bbMult);

			if (bbUpper.Count == 0 || atr.Count == 0) return Hold("Indicator calculation failed");

			int idx = closes.Count - 1;
			decimal close = closes[idx];
			decimal ema = bbMiddle[idx] ?? close;
			decimal atrValue = atr[idx];

			// Keltner Channels
			decimal kcUpper = ema + (kcMult * atrValue);
			decimal kcLower = ema - (kcMult * atrValue);

			// Squeeze: Bollinger Bands inside Keltner Channels
			bool squeeze = bbUpper[idx] < kcUpper && bbLower[idx] > kcLower;
			bool prevSqueeze = idx > 0 && bbUpper[idx - 1] < (ema + (kcMult * atr[idx - 1]));

			// Momentum indicator (simplified)
			decimal momentum = close - ema;
			decimal prevMomentum = idx > 0 ? closes[idx - 1] - (bbMiddle[idx - 1] ?? closes[idx - 1]) : 0;

			// Squeeze release with bullish momentum
			if (prevSqueeze && !squeeze && momentum > 0 && momentum > prevMomentum)
			{
				decimal strength = Clamp01(Math.Abs(momentum / close) * 20m + 0.6m);
				return new("Buy", strength, "Squeeze release: Bullish breakout with momentum");
			}

			// Squeeze release with bearish momentum
			if (prevSqueeze && !squeeze && momentum < 0 && momentum < prevMomentum)
			{
				decimal strength = Clamp01(Math.Abs(momentum / close) * 20m + 0.6m);
				return new("Sell", strength, "Squeeze release: Bearish breakdown with momentum");
			}

			if (squeeze)
				return Hold("In squeeze: Consolidation, waiting for breakout");

			return Hold("No squeeze setup");
		}

		// ═══════════════════════════════════════════════════════════════
		// 5) Money Flow Index (MFI) - Volume-Weighted RSI
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal MoneyFlowIndex(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes,
			int period = 14)
		{
			if (closes.Count < period + 5 || volumes == null || volumes.Count != closes.Count)
				return Hold("Insufficient data for MFI");

			var mfi = CalculateMFI(highs, lows, closes, volumes, period);

			if (mfi.Count < 3) return Hold("MFI calculation incomplete");

			decimal mfiNow = mfi.Last();
			decimal mfiPrev = mfi[mfi.Count - 2];
			decimal mfiPrev2 = mfi[mfi.Count - 3];

			// Oversold reversal
			if (mfiPrev <= 20m && mfiNow > 20m && mfiNow > mfiPrev && mfiPrev > mfiPrev2)
			{
				decimal strength = Clamp01((mfiNow - 20m) / 30m + 0.6m);
				return new("Buy", strength, $"MFI oversold reversal (MFI={mfiNow:F1})");
			}

			// Overbought reversal
			if (mfiPrev >= 80m && mfiNow < 80m && mfiNow < mfiPrev && mfiPrev < mfiPrev2)
			{
				decimal strength = Clamp01((80m - mfiNow) / 30m + 0.6m);
				return new("Sell", strength, $"MFI overbought reversal (MFI={mfiNow:F1})");
			}

			// Divergence detection
			bool bullishDiv = closes.Last() < closes[closes.Count - 10] && mfiNow > mfi[mfi.Count - 10];
			bool bearishDiv = closes.Last() > closes[closes.Count - 10] && mfiNow < mfi[mfi.Count - 10];

			if (bullishDiv && mfiNow < 40m)
			{
				return new("Buy", 0.75m, $"Bullish MFI divergence (MFI={mfiNow:F1})");
			}

			if (bearishDiv && mfiNow > 60m)
			{
				return new("Sell", 0.75m, $"Bearish MFI divergence (MFI={mfiNow:F1})");
			}

			return Hold($"MFI neutral ({mfiNow:F1})");
		}

		private static List<decimal> CalculateMFI(List<decimal> highs, List<decimal> lows,
			List<decimal> closes, List<decimal> volumes, int period)
		{
			var mfi = new List<decimal>();
			var typicalPrices = new List<decimal>();
			var moneyFlows = new List<decimal>();

			for (int i = 0; i < closes.Count; i++)
			{
				decimal tp = (highs[i] + lows[i] + closes[i]) / 3m;
				typicalPrices.Add(tp);
				moneyFlows.Add(tp * volumes[i]);
			}

			for (int i = period; i < closes.Count; i++)
			{
				decimal positiveFlow = 0m;
				decimal negativeFlow = 0m;

				for (int j = i - period + 1; j <= i; j++)
				{
					if (typicalPrices[j] > typicalPrices[j - 1])
						positiveFlow += moneyFlows[j];
					else if (typicalPrices[j] < typicalPrices[j - 1])
						negativeFlow += moneyFlows[j];
				}

				decimal mfiValue = negativeFlow == 0 ? 100m :
					100m - (100m / (1m + (positiveFlow / negativeFlow)));

				mfi.Add(Math.Round(mfiValue, 2));
			}

			return mfi;
		}

		// ═══════════════════════════════════════════════════════════════
		// 6) Parabolic SAR Strategy
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal ParabolicSAR(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			decimal acceleration = 0.02m,
			decimal maximum = 0.2m)
		{
			if (closes.Count < 10) return Hold("Insufficient data for SAR");

			var sar = CalculateParabolicSAR(highs, lows, acceleration, maximum);

			if (sar.Count < 3) return Hold("SAR calculation incomplete");

			int idx = closes.Count - 1;
			decimal price = closes[idx];
			decimal sarNow = sar[idx];
			decimal sarPrev = sar[idx - 1];

			bool bullish = price > sarNow;
			bool wasBullish = closes[idx - 1] > sarPrev;

			// Bullish SAR flip
			if (bullish && !wasBullish)
			{
				decimal distance = Math.Abs(price - sarNow) / price;
				decimal strength = Clamp01(distance * 50m + 0.65m);
				return new("Buy", strength, $"SAR flip bullish (SAR=${sarNow:F2})");
			}

			// Bearish SAR flip
			if (!bullish && wasBullish)
			{
				decimal distance = Math.Abs(price - sarNow) / price;
				decimal strength = Clamp01(distance * 50m + 0.65m);
				return new("Sell", strength, $"SAR flip bearish (SAR=${sarNow:F2})");
			}

			return Hold($"SAR {(bullish ? "bullish" : "bearish")} (no flip)");
		}

		private static List<decimal> CalculateParabolicSAR(List<decimal> highs, List<decimal> lows,
			decimal acceleration, decimal maximum)
		{
			var sar = new List<decimal>();
			bool isLong = true;
			decimal af = acceleration;
			decimal ep = highs[0];
			decimal sarValue = lows[0];

			sar.Add(sarValue);

			for (int i = 1; i < highs.Count; i++)
			{
				sarValue = sarValue + af * (ep - sarValue);

				if (isLong)
				{
					if (lows[i] < sarValue)
					{
						isLong = false;
						sarValue = ep;
						ep = lows[i];
						af = acceleration;
					}
					else
					{
						if (highs[i] > ep)
						{
							ep = highs[i];
							af = Math.Min(af + acceleration, maximum);
						}
					}
				}
				else
				{
					if (highs[i] > sarValue)
					{
						isLong = true;
						sarValue = ep;
						ep = highs[i];
						af = acceleration;
					}
					else
					{
						if (lows[i] < ep)
						{
							ep = lows[i];
							af = Math.Min(af + acceleration, maximum);
						}
					}
				}

				sar.Add(Math.Round(sarValue, 4));
			}

			return sar;
		}

		// ═══════════════════════════════════════════════════════════════
		// 7) Triple EMA Crossover (3 timeframes for confluence)
		// ═══════════════════════════════════════════════════════════════
		public static StrategySignal TripleEMA(
			List<decimal> closes,
			int fast = 8,
			int medium = 21,
			int slow = 50)
		{
			if (closes.Count < slow + 5) return Hold("Insufficient data for Triple EMA");

			var emaFast = Indicators.EMAList(closes, fast);
			var emaMedium = Indicators.EMAList(closes, medium);
			var emaSlow = Indicators.EMAList(closes, slow);

			int idx = closes.Count - 1;

			bool bullishAlignment = emaFast[idx] > emaMedium[idx] && emaMedium[idx] > emaSlow[idx];
			bool bearishAlignment = emaFast[idx] < emaMedium[idx] && emaMedium[idx] < emaSlow[idx];

			bool wasBullish = idx > 0 && emaFast[idx - 1] > emaMedium[idx - 1] &&
							 emaMedium[idx - 1] > emaSlow[idx - 1];
			bool wasBearish = idx > 0 && emaFast[idx - 1] < emaMedium[idx - 1] &&
							 emaMedium[idx - 1] < emaSlow[idx - 1];

			// Perfect bullish alignment with crossover
			if (bullishAlignment && !wasBullish)
			{
				decimal strength = 0.85m;
				return new("Buy", strength, "Triple EMA bullish alignment confirmed");
			}

			// Perfect bearish alignment with crossover
			if (bearishAlignment && !wasBearish)
			{
				decimal strength = 0.85m;
				return new("Sell", strength, "Triple EMA bearish alignment confirmed");
			}

			// Sustained trend
			if (bullishAlignment && wasBullish)
			{
				return new("Buy", 0.7m, "Triple EMA sustained uptrend");
			}

			if (bearishAlignment && wasBearish)
			{
				return new("Sell", 0.7m, "Triple EMA sustained downtrend");
			}

			return Hold("Triple EMA mixed or consolidating");
		}
	}
}