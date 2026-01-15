using System;
using System.Collections.Generic;
using System.Linq;
using static TraderBotV1.SignalValidator;

namespace TraderBotV1
{
	// ═══════════════════════════════════════════════════════════════
	// UNIFIED INDICATORS - All technical indicators in one place
	// ═══════════════════════════════════════════════════════════════
	public static class Indicators
	{
		// ──────────────────────────────────────────────────────────
		// BASIC INDICATORS
		// ──────────────────────────────────────────────────────────

		/// <summary>Simple Moving Average</summary>
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

		/// <summary>Exponential Moving Average (List)</summary>
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

		/// <summary>Exponential Moving Average (Single Value)</summary>
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

		/// <summary>Relative Strength Index (Wilder's Method)</summary>
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

		/// <summary>Moving Average Convergence Divergence - SWING OPTIMIZED</summary>
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

		/// <summary>Bollinger Bands - SWING OPTIMIZED (25 period default)</summary>
		public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
			BollingerBandsFast(List<decimal> closes, int period = 25, decimal mult = 2m)
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

		/// <summary>Average True Range</summary>
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

		// ──────────────────────────────────────────────────────────
		// EXTENDED INDICATORS
		// ──────────────────────────────────────────────────────────

		/// <summary>Stochastic RSI</summary>
		public static (List<decimal> k, List<decimal> d) StochRSIList(
			List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
		{
			int n = closes?.Count ?? 0;
			var kList = new List<decimal>(new decimal[n]);
			var dList = new List<decimal>(new decimal[n]);

			if (n < rsiPeriod + stochPeriod + Math.Max(smoothK, smoothD))
				return (kList, dList);

			var rsiVals = RSIList(closes, rsiPeriod);
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

		/// <summary>Average Directional Index</summary>
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

		/// <summary>Commodity Channel Index</summary>
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

		/// <summary>Donchian Channel</summary>
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

		/// <summary>Pivot Points (Classic)</summary>
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

		// ──────────────────────────────────────────────────────────
		// ENHANCED INDICATORS - Market Regime & Advanced Analysis
		// ──────────────────────────────────────────────────────────

		public enum MarketRegime
		{
			StrongTrending,
			WeakTrending,
			Ranging,
			Volatile,
			Quiet
		}

		public class MarketRegimeAnalysis
		{
			public MarketRegime Regime { get; set; }
			public decimal TrendStrength { get; set; }
			public decimal VolatilityLevel { get; set; }
			public bool IsTrendingUp { get; set; }
			public bool IsTrendingDown { get; set; }
			public decimal RegimeConfidence { get; set; }
			public string Description { get; set; } = "";
		}

		/// <summary>Advanced market regime detection</summary>
		public static MarketRegimeAnalysis DetectMarketRegime(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			int lookback = 50)
		{
			if (closes.Count < lookback + 50)
				return new MarketRegimeAnalysis { Regime = MarketRegime.Quiet };

			int idx = closes.Count - 1;

			// Calculate ADX for trend strength
			var (adx, diPlus, diMinus) = ADXList(highs, lows, closes, 14);
			decimal adxValue = adx.Count > idx ? adx[idx] : 0m;

			// Calculate volatility metrics
			var atr = ATRList(highs, lows, closes, 14);
			decimal currentATR = atr.Count > idx ? atr[idx] : 0m;
			decimal price = closes[idx];
			decimal atrPercent = price > 0 ? (currentATR / price) : 0m;

			// Calculate price movement
			var recentPrices = closes.Skip(Math.Max(0, idx - lookback)).Take(lookback).ToList();
			decimal priceRange = recentPrices.Max() - recentPrices.Min();
			decimal avgPrice = recentPrices.Average();
			decimal rangePercent = avgPrice > 0 ? (priceRange / avgPrice) : 0m;

			// EMA alignment check
			var ema20 = EMA(closes.Take(idx + 1).ToList(), 20);
			var ema50 = EMA(closes.Take(idx + 1).ToList(), 50);
			var ema200 = idx >= 200 ? EMA(closes.Take(idx + 1).ToList(), 200) : ema50;

			bool strongUpTrend = ema20 > ema50 && ema50 > ema200 &&
				diPlus.Count > idx && diMinus.Count > idx && diPlus[idx] > diMinus[idx];
			bool strongDownTrend = ema20 < ema50 && ema50 < ema200 &&
				diPlus.Count > idx && diMinus.Count > idx && diMinus[idx] > diPlus[idx];

			// Linear regression slope for trend
			decimal slope = CalculateLinearRegressionSlope(recentPrices);
			decimal slopePercent = avgPrice > 0 ? Math.Abs(slope / avgPrice) * 100 : 0m;

			// Regime classification
			MarketRegime regime;
			decimal regimeConfidence;
			string description;

			if (adxValue > 30 && slopePercent > 0.5m)
			{
				regime = MarketRegime.StrongTrending;
				regimeConfidence = Math.Min((adxValue - 30) / 20m, 1m);
				description = strongUpTrend ? "Strong Uptrend" : strongDownTrend ? "Strong Downtrend" : "Strong Trend";
			}
			else if (adxValue > 20 && slopePercent > 0.2m)
			{
				regime = MarketRegime.WeakTrending;
				regimeConfidence = (adxValue - 20) / 10m;
				description = "Weak Trend";
			}
			else if (adxValue < 20 && rangePercent < 0.05m)
			{
				regime = MarketRegime.Ranging;
				regimeConfidence = (20m - adxValue) / 20m;
				description = "Ranging Market";
			}
			else if (atrPercent > 0.03m)
			{
				regime = MarketRegime.Volatile;
				regimeConfidence = Math.Min(atrPercent / 0.05m, 1m);
				description = "High Volatility";
			}
			else
			{
				regime = MarketRegime.Quiet;
				regimeConfidence = 0.5m;
				description = "Quiet Market";
			}

			return new MarketRegimeAnalysis
			{
				Regime = regime,
				TrendStrength = slopePercent,
				VolatilityLevel = atrPercent,
				IsTrendingUp = strongUpTrend,
				IsTrendingDown = strongDownTrend,
				RegimeConfidence = regimeConfidence,
				Description = description
			};
		}

		public class MultiTimeframeSignal
		{
			public string HigherTFTrend { get; set; } = "Neutral";
			public string CurrentTFTrend { get; set; } = "Neutral";
			public bool IsAligned { get; set; }
			public decimal Confidence { get; set; }
			public string Reason { get; set; } = "";
		}

		/// <summary>Multi-timeframe trend analysis</summary>
		public static MultiTimeframeSignal AnalyzeMultiTimeframe(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			if (closes.Count < 100)
				return new MultiTimeframeSignal { Reason = "Insufficient data" };

			// Current timeframe trend
			var currentEma20 = EMA(closes, 20);
			var currentEma50 = EMA(closes, 50);
			string currentTrend = currentEma20 > currentEma50 ? "Up" : currentEma20 < currentEma50 ? "Down" : "Neutral";

			// Simulate higher timeframe by taking every 5th bar
			var htfCloses = new List<decimal>();
			var htfHighs = new List<decimal>();
			var htfLows = new List<decimal>();

			for (int i = 0; i < closes.Count; i += 5)
			{
				htfCloses.Add(closes[i]);
				htfHighs.Add(highs[i]);
				htfLows.Add(lows[i]);
			}

			if (htfCloses.Count < 50)
				return new MultiTimeframeSignal { CurrentTFTrend = currentTrend, Reason = "Insufficient HTF data" };

			// Higher timeframe trend
			var htfEma20 = EMA(htfCloses, 20);
			var htfEma50 = EMA(htfCloses, 50);
			string htfTrend = htfEma20 > htfEma50 ? "Up" : htfEma20 < htfEma50 ? "Down" : "Neutral";

			// Check alignment
			bool aligned = currentTrend == htfTrend && currentTrend != "Neutral";

			// Calculate confidence based on separation
			decimal currentSeparation = Math.Abs(currentEma20 - currentEma50) / currentEma50;
			decimal htfSeparation = Math.Abs(htfEma20 - htfEma50) / htfEma50;
			decimal confidence = aligned ? Math.Min((currentSeparation + htfSeparation) * 50m, 1m) : 0.3m;

			return new MultiTimeframeSignal
			{
				HigherTFTrend = htfTrend,
				CurrentTFTrend = currentTrend,
				IsAligned = aligned,
				Confidence = confidence,
				Reason = aligned ? $"Trends aligned {currentTrend}" : $"Trends divergent (HTF:{htfTrend}, CTF:{currentTrend})"
			};
		}

		public class SupportResistanceLevel
		{
			public decimal Level { get; set; }
			public int Touches { get; set; }
			public decimal Strength { get; set; }
			public bool IsSupport { get; set; }
		}

		/// <summary>Identifies dynamic support and resistance levels</summary>
		public static List<SupportResistanceLevel> FindSupportResistance(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			int lookback = 50,
			decimal tolerance = 0.02m)
		{
			var levels = new List<SupportResistanceLevel>();
			if (closes.Count < lookback) return levels;

			int startIdx = Math.Max(0, closes.Count - lookback);
			var recentHighs = highs.Skip(startIdx).ToList();
			var recentLows = lows.Skip(startIdx).ToList();

			// Find swing highs (potential resistance)
			for (int i = 2; i < recentHighs.Count - 2; i++)
			{
				if (recentHighs[i] > recentHighs[i - 1] && recentHighs[i] > recentHighs[i - 2] &&
					recentHighs[i] > recentHighs[i + 1] && recentHighs[i] > recentHighs[i + 2])
				{
					decimal level = recentHighs[i];
					int touches = CountTouches(closes, level, tolerance);

					if (touches >= 2)
					{
						levels.Add(new SupportResistanceLevel
						{
							Level = level,
							Touches = touches,
							Strength = Math.Min(touches / 5m, 1m),
							IsSupport = false
						});
					}
				}
			}

			// Find swing lows (potential support)
			for (int i = 2; i < recentLows.Count - 2; i++)
			{
				if (recentLows[i] < recentLows[i - 1] && recentLows[i] < recentLows[i - 2] &&
					recentLows[i] < recentLows[i + 1] && recentLows[i] < recentLows[i + 2])
				{
					decimal level = recentLows[i];
					int touches = CountTouches(closes, level, tolerance);

					if (touches >= 2)
					{
						levels.Add(new SupportResistanceLevel
						{
							Level = level,
							Touches = touches,
							Strength = Math.Min(touches / 5m, 1m),
							IsSupport = true
						});
					}
				}
			}

			// Cluster nearby levels
			var clustered = new List<SupportResistanceLevel>();
			var sorted = levels.OrderBy(l => l.Level).ToList();
			var used = new bool[sorted.Count];

			for (int i = 0; i < sorted.Count; i++)
			{
				if (used[i]) continue;

				var cluster = new List<SupportResistanceLevel> { sorted[i] };
				used[i] = true;

				for (int j = i + 1; j < sorted.Count; j++)
				{
					if (used[j]) continue;

					if (Math.Abs(sorted[j].Level - sorted[i].Level) / sorted[i].Level <= tolerance)
					{
						cluster.Add(sorted[j]);
						used[j] = true;
					}
				}

				if (cluster.Count > 0)
				{
					clustered.Add(new SupportResistanceLevel
					{
						Level = cluster.Average(c => c.Level),
						Touches = cluster.Sum(c => c.Touches),
						Strength = cluster.Average(c => c.Strength),
						IsSupport = cluster.First().IsSupport
					});
				}
			}

			return clustered.OrderByDescending(l => l.Strength).ToList();
		}

		public class VolumeAnalysis
		{
			public bool IsAccumulation { get; set; }
			public bool IsDistribution { get; set; }
			public decimal VolumeStrength { get; set; }
			public decimal VolumeRatio { get; set; }
			public string Signal { get; set; } = "Neutral";
		}

		/// <summary>Advanced volume analysis using OBV</summary>
		public static VolumeAnalysis AnalyzeVolume(
			List<decimal> closes,
			List<decimal> volumes,
			int lookback = 20)
		{
			if (volumes == null || volumes.Count < lookback)
				return new VolumeAnalysis();

			int idx = closes.Count - 1;

			// Calculate On-Balance Volume
			var obv = CalculateOBV(closes, volumes);
			var obvEma = EMAList(obv, 10);

			// Volume ratio
			decimal currentVol = volumes[idx];
			var recentVol = volumes.Skip(Math.Max(0, idx - lookback)).Take(lookback);
			decimal avgVol = recentVol.Average();
			decimal volRatio = avgVol > 0 ? currentVol / avgVol : 1m;

			// Price direction
			bool priceUp = closes[idx] > closes[idx - 1];

			// OBV trend
			bool obvRising = obv.Count >= 3 && obv[^1] > obv[^2] && obv[^2] > obv[^3];
			bool obvFalling = obv.Count >= 3 && obv[^1] < obv[^2] && obv[^2] < obv[^3];

			// Determine accumulation/distribution
			bool isAccumulation = priceUp && obvRising && volRatio > 1.2m;
			bool isDistribution = !priceUp && obvFalling && volRatio > 1.2m;

			string signal = "Neutral";
			if (isAccumulation) signal = "Bullish";
			else if (isDistribution) signal = "Bearish";

			return new VolumeAnalysis
			{
				IsAccumulation = isAccumulation,
				IsDistribution = isDistribution,
				VolumeStrength = Math.Min(volRatio, 3m) / 3m,
				VolumeRatio = volRatio,
				Signal = signal
			};
		}

		public class CandlestickPattern
		{
			public string PatternName { get; set; } = "";
			public string Signal { get; set; } = "Neutral";
			public decimal Strength { get; set; }
		}

		/// <summary>Recognizes key candlestick patterns</summary>
		public static List<CandlestickPattern> RecognizePatterns(
			List<decimal> opens,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes)
		{
			var patterns = new List<CandlestickPattern>();
			int idx = closes.Count - 1;

			if (idx < 3) return patterns;

			// Hammer / Hanging Man
			decimal body = Math.Abs(closes[idx] - opens[idx]);
			decimal range = highs[idx] - lows[idx];
			decimal lowerShadow = Math.Min(opens[idx], closes[idx]) - lows[idx];
			decimal upperShadow = highs[idx] - Math.Max(opens[idx], closes[idx]);

			if (range > 0 && lowerShadow > body * 2 && upperShadow < body * 0.3m)
			{
				bool bullish = closes[idx] > opens[idx];
				patterns.Add(new CandlestickPattern
				{
					PatternName = bullish ? "Hammer" : "Hanging Man",
					Signal = bullish ? "Bullish" : "Bearish",
					Strength = Math.Min(lowerShadow / range, 1m)
				});
			}

			// Engulfing Pattern
			if (idx >= 1)
			{
				bool prevBullish = closes[idx - 1] > opens[idx - 1];
				bool currBullish = closes[idx] > opens[idx];
				decimal prevBody = Math.Abs(closes[idx - 1] - opens[idx - 1]);
				decimal currBody = Math.Abs(closes[idx] - opens[idx]);

				if (!prevBullish && currBullish && currBody > prevBody * 1.5m &&
					opens[idx] < closes[idx - 1] && closes[idx] > opens[idx - 1])
				{
					patterns.Add(new CandlestickPattern
					{
						PatternName = "Bullish Engulfing",
						Signal = "Bullish",
						Strength = Math.Min(currBody / prevBody / 2m, 1m)
					});
				}
				else if (prevBullish && !currBullish && currBody > prevBody * 1.5m &&
					opens[idx] > closes[idx - 1] && closes[idx] < opens[idx - 1])
				{
					patterns.Add(new CandlestickPattern
					{
						PatternName = "Bearish Engulfing",
						Signal = "Bearish",
						Strength = Math.Min(currBody / prevBody / 2m, 1m)
					});
				}
			}

			// Doji
			if (body < range * 0.1m && range > 0)
			{
				patterns.Add(new CandlestickPattern
				{
					PatternName = "Doji",
					Signal = "Neutral",
					Strength = 0.6m
				});
			}

			// Morning/Evening Star
			if (idx >= 2)
			{
				decimal body0 = Math.Abs(closes[idx - 2] - opens[idx - 2]);
				decimal body1 = Math.Abs(closes[idx - 1] - opens[idx - 1]);
				decimal body2 = Math.Abs(closes[idx] - opens[idx]);

				// Morning Star
				if (closes[idx - 2] < opens[idx - 2] && body1 < body0 * 0.3m &&
					closes[idx] > opens[idx] && closes[idx] > (opens[idx - 2] + closes[idx - 2]) / 2)
				{
					patterns.Add(new CandlestickPattern
					{
						PatternName = "Morning Star",
						Signal = "Bullish",
						Strength = 0.85m
					});
				}
				// Evening Star
				else if (closes[idx - 2] > opens[idx - 2] && body1 < body0 * 0.3m &&
					closes[idx] < opens[idx] && closes[idx] < (opens[idx - 2] + closes[idx - 2]) / 2)
				{
					patterns.Add(new CandlestickPattern
					{
						PatternName = "Evening Star",
						Signal = "Bearish",
						Strength = 0.85m
					});
				}
			}

			return patterns;
		}

		// ──────────────────────────────────────────────────────────
		// PRIVATE HELPER METHODS
		// ──────────────────────────────────────────────────────────

		private static int CountTouches(List<decimal> closes, decimal level, decimal tolerance)
		{
			int touches = 0;
			foreach (var price in closes)
			{
				if (Math.Abs(price - level) / level <= tolerance)
					touches++;
			}
			return touches;
		}

		private static List<decimal> CalculateOBV(List<decimal> closes, List<decimal> volumes)
		{
			var obv = new List<decimal> { 0 };

			for (int i = 1; i < closes.Count; i++)
			{
				decimal change = closes[i] > closes[i - 1] ? volumes[i] :
								closes[i] < closes[i - 1] ? -volumes[i] : 0;
				obv.Add(obv[i - 1] + change);
			}

			return obv;
		}

		private static decimal CalculateLinearRegressionSlope(List<decimal> values)
		{
			int n = values.Count;
			if (n < 2) return 0;

			decimal sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

			for (int i = 0; i < n; i++)
			{
				decimal x = i;
				decimal y = values[i];
				sumX += x;
				sumY += y;
				sumXY += x * y;
				sumX2 += x * x;
			}

			decimal denominator = n * sumX2 - sumX * sumX;
			if (denominator == 0) return 0;

			return (n * sumXY - sumX * sumY) / denominator;
		}


		/// <summary> 11/8/2026
		/// NEW INDICATORS - Enhanced technical analysis indicators
		/// Add these to your existing Indicators.cs or use as a separate file
		/// </summary>

		// ══════════════════════════════════════════════════════════════════
		// VOLUME-BASED INDICATORS
		// ══════════════════════════════════════════════════════════════════

		/// <summary>Money Flow Index - Volume-weighted RSI</summary>
		public static List<decimal> MFIList(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			List<decimal> volumes,
			int period = 14)
		{
			var result = new List<decimal>();
			if (closes.Count < period + 1 || volumes.Count != closes.Count)
				return result;

			var typicalPrices = new List<decimal>();
			var rawMoneyFlow = new List<decimal>();

			// Calculate typical price and money flow
			for (int i = 0; i < closes.Count; i++)
			{
				decimal tp = (highs[i] + lows[i] + closes[i]) / 3m;
				typicalPrices.Add(tp);
				rawMoneyFlow.Add(tp * volumes[i]);
			}

			// Calculate MFI for each period
			for (int i = period; i < closes.Count; i++)
			{
				decimal positiveFlow = 0m;
				decimal negativeFlow = 0m;

				for (int j = i - period + 1; j <= i; j++)
				{
					if (typicalPrices[j] > typicalPrices[j - 1])
						positiveFlow += rawMoneyFlow[j];
					else if (typicalPrices[j] < typicalPrices[j - 1])
						negativeFlow += rawMoneyFlow[j];
				}

				decimal mfi = negativeFlow == 0 ? 100m :
					100m - (100m / (1m + (positiveFlow / Math.Max(negativeFlow, 1e-10m))));

				result.Add(Math.Round(mfi, 2));
			}

			return result;
		}

		/// <summary>Chaikin Money Flow - Measures buying/selling pressure</summary>
		public static List<decimal> CMFList(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			List<decimal> volumes,
			int period = 20)
		{
			var result = new List<decimal>();
			if (closes.Count < period || volumes.Count != closes.Count)
				return result;

			var moneyFlowVolume = new List<decimal>();

			// Calculate Money Flow Volume for each bar
			for (int i = 0; i < closes.Count; i++)
			{
				decimal range = highs[i] - lows[i];
				if (range == 0)
				{
					moneyFlowVolume.Add(0m);
					continue;
				}

				// Money Flow Multiplier
				decimal mfMultiplier = ((closes[i] - lows[i]) - (highs[i] - closes[i])) / range;
				decimal mfv = mfMultiplier * volumes[i];
				moneyFlowVolume.Add(mfv);
			}

			// Calculate CMF for each period
			for (int i = period - 1; i < closes.Count; i++)
			{
				decimal mfvSum = moneyFlowVolume.Skip(i - period + 1).Take(period).Sum();
				decimal volumeSum = volumes.Skip(i - period + 1).Take(period).Sum();

				decimal cmf = volumeSum == 0 ? 0m : mfvSum / volumeSum;
				result.Add(Math.Round(cmf, 4));
			}

			return result;
		}

		/// <summary>Elder's Force Index - Combines price, volume, and momentum</summary>
		public static List<decimal> ForceIndexList(
			List<decimal> closes,
			List<decimal> volumes,
			int period = 13)
		{
			if (closes.Count < 2 || volumes.Count != closes.Count)
				return new List<decimal>();

			var rawForce = new List<decimal>();

			// Calculate raw force
			for (int i = 1; i < closes.Count; i++)
			{
				decimal force = (closes[i] - closes[i - 1]) * volumes[i];
				rawForce.Add(force);
			}

			// Smooth with EMA
			var smoothedForce = Indicators.EMAList(rawForce, period);

			// Pad with zeros at the beginning
			smoothedForce.Insert(0, 0m);

			return smoothedForce;
		}

		// ══════════════════════════════════════════════════════════════════
		// MOMENTUM INDICATORS
		// ══════════════════════════════════════════════════════════════════

		/// <summary>Rate of Change - Pure momentum indicator</summary>
		public static List<decimal> ROCList(List<decimal> closes, int period = 12)
		{
			var result = new List<decimal>();
			if (closes.Count < period + 1)
				return result;

			for (int i = period; i < closes.Count; i++)
			{
				decimal roc = ((closes[i] - closes[i - period]) / closes[i - period]) * 100m;
				result.Add(Math.Round(roc, 2));
			}

			return result;
		}

		/// <summary>True Strength Index - Double-smoothed momentum</summary>
		public static (List<decimal> tsi, List<decimal> signal) TSIList(
			List<decimal> closes,
			int longPeriod = 25,
			int shortPeriod = 13,
			int signalPeriod = 7)
		{
			var tsiValues = new List<decimal>();
			var signalLine = new List<decimal>();

			if (closes.Count < longPeriod + shortPeriod + signalPeriod)
				return (tsiValues, signalLine);

			// Calculate price changes
			var priceChanges = new List<decimal>();
			var absChanges = new List<decimal>();
			for (int i = 1; i < closes.Count; i++)
			{
				decimal change = closes[i] - closes[i - 1];
				priceChanges.Add(change);
				absChanges.Add(Math.Abs(change));
			}

			// Double smoothing of absolute price changes
			var singleSmoothAbs = Indicators.EMAList(absChanges, longPeriod);
			var doubleSmoothAbs = Indicators.EMAList(singleSmoothAbs, shortPeriod);

			// Double smoothing of price changes
			var singleSmoothChange = Indicators.EMAList(priceChanges, longPeriod);
			var doubleSmoothChange = Indicators.EMAList(singleSmoothChange, shortPeriod);

			// Calculate TSI
			int minLength = Math.Min(doubleSmoothAbs.Count, doubleSmoothChange.Count);
			for (int i = 0; i < minLength; i++)
			{
				if (doubleSmoothAbs[i] == 0)
					tsiValues.Add(0m);
				else
				{
					decimal tsi = 100m * (doubleSmoothChange[i] / doubleSmoothAbs[i]);
					tsiValues.Add(Math.Round(tsi, 2));
				}
			}

			// Calculate signal line
			if (tsiValues.Count >= signalPeriod)
				signalLine = Indicators.EMAList(tsiValues, signalPeriod);

			return (tsiValues, signalLine);
		}

		// ══════════════════════════════════════════════════════════════════
		// TREND INDICATORS
		// ══════════════════════════════════════════════════════════════════

		/// <summary>
		/// Supertrend Indicator - Strong trend following indicator
		/// Returns: (supertrend values, direction list where 1=up, -1=down)
		/// </summary>
		public static (List<decimal> supertrend, List<int> direction) SupertrendList(
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> closes,
			int period = 10,
			decimal multiplier = 3m)
		{
			var supertrendValues = new List<decimal>();
			var direction = new List<int>();

			if (closes.Count < period + 10)
				return (supertrendValues, direction);

			var atr = Indicators.ATRList(highs, lows, closes, period);
			if (atr.Count == 0)
				return (supertrendValues, direction);

			var basicUpperBand = new List<decimal>();
			var basicLowerBand = new List<decimal>();
			var finalUpperBand = new List<decimal>();
			var finalLowerBand = new List<decimal>();

			for (int i = 0; i < closes.Count; i++)
			{
				decimal hl2 = (highs[i] + lows[i]) / 2m;
				decimal atrValue = i < atr.Count ? atr[i] : 0m;

				decimal basicUpper = hl2 + multiplier * atrValue;
				decimal basicLower = hl2 - multiplier * atrValue;

				basicUpperBand.Add(basicUpper);
				basicLowerBand.Add(basicLower);

				// Calculate final bands
				decimal finalUpper = i == 0 || basicUpper < finalUpperBand[i - 1] ||
					closes[i - 1] > finalUpperBand[i - 1] ? basicUpper : finalUpperBand[i - 1];

				decimal finalLower = i == 0 || basicLower > finalLowerBand[i - 1] ||
					closes[i - 1] < finalLowerBand[i - 1] ? basicLower : finalLowerBand[i - 1];

				finalUpperBand.Add(finalUpper);
				finalLowerBand.Add(finalLower);

				// Determine direction
				int currentDirection = i == 0 ? 1 : direction[i - 1];
				if (currentDirection == -1 && closes[i] > finalUpper)
					currentDirection = 1;
				else if (currentDirection == 1 && closes[i] < finalLower)
					currentDirection = -1;

				direction.Add(currentDirection);

				// Supertrend value
				decimal stValue = currentDirection == 1 ? finalLower : finalUpper;
				supertrendValues.Add(Math.Round(stValue, 4));
			}

			return (supertrendValues, direction);
		}

		// ══════════════════════════════════════════════════════════════════
		// UTILITY INDICATORS
		// ══════════════════════════════════════════════════════════════════

		/// <summary>Bollinger %B - Normalized position within Bollinger Bands</summary>
		public static List<decimal?> BollingerPercentB(
			List<decimal> closes,
			List<decimal?> upper,
			List<decimal?> lower)
		{
			var result = new List<decimal?>();

			for (int i = 0; i < closes.Count; i++)
			{
				if (i >= upper.Count || i >= lower.Count ||
					upper[i] == null || lower[i] == null)
				{
					result.Add(null);
					continue;
				}

				decimal bandWidth = upper[i].Value - lower[i].Value;
				if (bandWidth == 0)
				{
					result.Add(0.5m);
					continue;
				}

				decimal percentB = (closes[i] - lower[i].Value) / bandWidth;
				result.Add(Math.Round(percentB, 4));
			}

			return result;
		}

		/// <summary>
		/// Estimate Volume from Price Action
		/// Use when actual volume data is unavailable
		/// </summary>
		public static List<decimal> EstimateVolume(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			var estimatedVolume = new List<decimal>();

			// First bar gets baseline
			estimatedVolume.Add(100000m);

			for (int i = 1; i < closes.Count; i++)
			{
				// Use True Range as proxy for activity
				decimal trueRange = Math.Max(
					highs[i] - lows[i],
					Math.Max(
						Math.Abs(highs[i] - closes[i - 1]),
						Math.Abs(lows[i] - closes[i - 1])
					)
				);

				// Price change magnitude
				decimal priceChange = Math.Abs(closes[i] - closes[i - 1]);

				// Combine for volume estimate
				// Higher volatility and larger moves = higher estimated volume
				decimal baseEstimate = 100000m;
				decimal volatilityMultiplier = 1m + (trueRange / closes[i]);
				decimal momentumMultiplier = 1m + (priceChange / closes[i - 1]);

				decimal estimatedVol = baseEstimate * volatilityMultiplier * momentumMultiplier;
				estimatedVolume.Add(Math.Round(estimatedVol, 0));
			}

			return estimatedVolume;
		}

		// ══════════════════════════════════════════════════════════════════
		// VALIDATION HELPERS FOR NEW INDICATORS
		// ══════════════════════════════════════════════════════════════════

		/// <summary>Validate MFI signal</summary>
		public static SignalValidation ValidateMFI(
			List<decimal> mfi,
			int idx,
			string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 2 || mfi.Count <= idx)
				return validation.Fail("Insufficient MFI data");

			decimal currentMFI = mfi[idx];
			decimal prevMFI = mfi[idx - 1];
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// Oversold and turning up
				if (currentMFI < 25m && currentMFI > prevMFI)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m + ((25m - currentMFI) / 25m) * 0.20m;
					validation.Reason = $"MFI oversold reversal ({currentMFI:F1})";
				}
				// Bullish divergence zone
				else if (currentMFI < 35m && currentMFI > prevMFI)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"MFI bullish momentum ({currentMFI:F1})";
				}
			}
			else
			{
				// Overbought and turning down
				if (currentMFI > 75m && currentMFI < prevMFI)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m + ((currentMFI - 75m) / 25m) * 0.20m;
					validation.Reason = $"MFI overbought reversal ({currentMFI:F1})";
				}
				// Bearish divergence zone
				else if (currentMFI > 65m && currentMFI < prevMFI)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"MFI bearish momentum ({currentMFI:F1})";
				}
			}

			return validation;
		}

		/// <summary>Validate Supertrend signal</summary>
		public static SignalValidation ValidateSupertrend(
			List<decimal> supertrend,
			List<int> direction,
			List<decimal> closes,
			int idx)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 2 || direction.Count <= idx)
				return validation.Fail("Insufficient Supertrend data");

			int currentDir = direction[idx];
			int prevDir = direction[idx - 1];

			// Check for direction change
			if (currentDir != prevDir)
			{
				validation.IsValid = true;

				if (currentDir == 1)
				{
					// Bullish flip
					validation.Confidence = 0.75m;
					validation.Reason = $"Supertrend bullish flip (${closes[idx]:F2} > ${supertrend[idx]:F2})";
				}
				else
				{
					// Bearish flip
					validation.Confidence = 0.75m;
					validation.Reason = $"Supertrend bearish flip (${closes[idx]:F2} < ${supertrend[idx]:F2})";
				}
			}

			return validation;
		}

		/// <summary>Validate CMF signal</summary>
		public static SignalValidation ValidateCMF(
			List<decimal> cmf,
			int idx,
			string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 3 || cmf.Count <= idx)
				return validation.Fail("Insufficient CMF data");

			decimal currentCMF = cmf[idx];
			decimal prevCMF = cmf[idx - 1];
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// Strong buying pressure
				if (currentCMF > 0.05m && currentCMF > prevCMF)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m + Math.Min(currentCMF * 2m, 0.30m);
					validation.Reason = $"CMF buying pressure ({currentCMF:F3})";
				}
			}
			else
			{
				// Strong selling pressure
				if (currentCMF < -0.05m && currentCMF < prevCMF)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m + Math.Min(Math.Abs(currentCMF) * 2m, 0.30m);
					validation.Reason = $"CMF selling pressure ({currentCMF:F3})";
				}
			}

			return validation;
		}

		// ──────────────────────────────────────────────────────────
		// 1. WILLIAMS %R - Momentum indicator (similar to Stochastic)
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Williams %R indicator - Shows overbought/oversold conditions
		/// Values range from 0 to -100 (inverted compared to Stochastic)
		/// </summary>
		public static List<decimal> WilliamsR(List<decimal> highs, List<decimal> lows,
			List<decimal> closes, int period = 14)
		{
			var result = new List<decimal>();
			if (closes.Count < period) return result;

			for (int i = period - 1; i < closes.Count; i++)
			{
				var periodHighs = highs.Skip(i - period + 1).Take(period).ToList();
				var periodLows = lows.Skip(i - period + 1).Take(period).ToList();

				decimal highestHigh = periodHighs.Max();
				decimal lowestLow = periodLows.Min();
				decimal close = closes[i];

				decimal denom = Math.Max(highestHigh - lowestLow, 1e-8m);
				decimal williamsR = -100m * (highestHigh - close) / denom;

				result.Add(Math.Round(williamsR, 2));
			}

			return result;
		}

		// ──────────────────────────────────────────────────────────
		// 2. PARABOLIC SAR - Trend following indicator
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Parabolic SAR - Stop and Reverse indicator for trend following
		/// Returns list of SAR values and trend direction
		/// </summary>
		public static (List<decimal> sar, List<bool> isBullish) ParabolicSAR(
			List<decimal> highs, List<decimal> lows, List<decimal> closes,
			decimal acceleration = 0.02m, decimal maxAcceleration = 0.2m)
		{
			var sar = new List<decimal>();
			var isBullish = new List<bool>();

			if (closes.Count < 5) return (sar, isBullish);

			// Initialize
			bool bull = closes[1] > closes[0];
			decimal af = acceleration;
			decimal ep = bull ? highs.Take(2).Max() : lows.Take(2).Min();
			decimal currentSar = bull ? lows.Take(2).Min() : highs.Take(2).Max();

			sar.Add(currentSar);
			isBullish.Add(bull);

			for (int i = 1; i < closes.Count; i++)
			{
				// Calculate new SAR
				decimal newSar = currentSar + af * (ep - currentSar);

				// Check for reversal
				bool reversal = false;
				if (bull)
				{
					newSar = Math.Min(newSar, lows[i - 1]);
					if (i > 1) newSar = Math.Min(newSar, lows[i - 2]);

					if (lows[i] < newSar)
					{
						reversal = true;
						bull = false;
						newSar = ep;
						ep = lows[i];
						af = acceleration;
					}
					else
					{
						if (highs[i] > ep)
						{
							ep = highs[i];
							af = Math.Min(af + acceleration, maxAcceleration);
						}
					}
				}
				else
				{
					newSar = Math.Max(newSar, highs[i - 1]);
					if (i > 1) newSar = Math.Max(newSar, highs[i - 2]);

					if (highs[i] > newSar)
					{
						reversal = true;
						bull = true;
						newSar = ep;
						ep = highs[i];
						af = acceleration;
					}
					else
					{
						if (lows[i] < ep)
						{
							ep = lows[i];
							af = Math.Min(af + acceleration, maxAcceleration);
						}
					}
				}

				currentSar = newSar;
				sar.Add(Math.Round(currentSar, 4));
				isBullish.Add(bull);
			}

			return (sar, isBullish);
		}

		// ──────────────────────────────────────────────────────────
		// 3. KELTNER CHANNELS - Volatility-based channels
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Keltner Channels - ATR-based volatility channels
		/// Alternative to Bollinger Bands
		/// </summary>
		public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
			KeltnerChannels(List<decimal> highs, List<decimal> lows, List<decimal> closes,
			int emaPeriod = 20, int atrPeriod = 10, decimal multiplier = 2m)
		{
			var upper = new List<decimal?>();
			var middle = new List<decimal?>();
			var lower = new List<decimal?>();

			if (closes.Count < Math.Max(emaPeriod, atrPeriod))
			{
				return (Enumerable.Repeat<decimal?>(null, closes.Count).ToList(),
						Enumerable.Repeat<decimal?>(null, closes.Count).ToList(),
						Enumerable.Repeat<decimal?>(null, closes.Count).ToList());
			}

			var ema = Indicators.EMAList(closes, emaPeriod);
			var atr = Indicators.ATRList(highs, lows, closes, atrPeriod);

			for (int i = 0; i < closes.Count; i++)
			{
				if (i < Math.Max(emaPeriod, atrPeriod) || atr[i] == 0)
				{
					upper.Add(null);
					middle.Add(null);
					lower.Add(null);
				}
				else
				{
					decimal centerLine = ema[i];
					decimal atrValue = atr[i];

					middle.Add(Math.Round(centerLine, 4));
					upper.Add(Math.Round(centerLine + multiplier * atrValue, 4));
					lower.Add(Math.Round(centerLine - multiplier * atrValue, 4));
				}
			}

			return (upper, middle, lower);
		}

		// ──────────────────────────────────────────────────────────
		// 4. ON-BALANCE VOLUME (OBV) - Volume momentum
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// On-Balance Volume - Cumulative volume momentum indicator
		/// Measures buying/selling pressure
		/// </summary>
		public static List<decimal> OBV(List<decimal> closes, List<decimal> volumes)
		{
			var obv = new List<decimal>();
			if (closes.Count == 0 || volumes.Count != closes.Count) return obv;

			decimal cumulativeOBV = 0m;
			obv.Add(cumulativeOBV); // First value is 0

			for (int i = 1; i < closes.Count; i++)
			{
				if (closes[i] > closes[i - 1])
					cumulativeOBV += volumes[i];
				else if (closes[i] < closes[i - 1])
					cumulativeOBV -= volumes[i];
				// If price unchanged, OBV stays same

				obv.Add(Math.Round(cumulativeOBV, 2));
			}

			return obv;
		}

		// ──────────────────────────────────────────────────────────
		// 5. AROON INDICATOR - Trend change detection
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Aroon Indicator - Identifies trend changes and strength
		/// Returns Aroon Up and Aroon Down
		/// </summary>
		public static (List<decimal> aroonUp, List<decimal> aroonDown, List<decimal> aroonOscillator)
			AroonIndicator(List<decimal> highs, List<decimal> lows, int period = 25)
		{
			var aroonUp = new List<decimal>();
			var aroonDown = new List<decimal>();
			var aroonOsc = new List<decimal>();

			if (highs.Count < period) return (aroonUp, aroonDown, aroonOsc);

			for (int i = period - 1; i < highs.Count; i++)
			{
				var periodHighs = highs.Skip(i - period + 1).Take(period).ToList();
				var periodLows = lows.Skip(i - period + 1).Take(period).ToList();

				// Find days since highest high and lowest low
				int daysSinceHigh = period - 1 - periodHighs.LastIndexOf(periodHighs.Max());
				int daysSinceLow = period - 1 - periodLows.LastIndexOf(periodLows.Min());

				decimal aroonUpValue = ((decimal)(period - daysSinceHigh) / period) * 100m;
				decimal aroonDownValue = ((decimal)(period - daysSinceLow) / period) * 100m;

				aroonUp.Add(Math.Round(aroonUpValue, 2));
				aroonDown.Add(Math.Round(aroonDownValue, 2));
				aroonOsc.Add(Math.Round(aroonUpValue - aroonDownValue, 2));
			}

			return (aroonUp, aroonDown, aroonOsc);
		}

		// ──────────────────────────────────────────────────────────
		// 6. RATE OF CHANGE (ROC) - Momentum indicator
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Rate of Change - Measures momentum as percentage change
		/// </summary>
		public static List<decimal> ROC(List<decimal> closes, int period = 12)
		{
			var roc = new List<decimal>();
			if (closes.Count < period + 1) return roc;

			for (int i = period; i < closes.Count; i++)
			{
				decimal previousClose = closes[i - period];
				if (previousClose == 0) previousClose = 1e-8m;

				decimal rocValue = ((closes[i] - previousClose) / previousClose) * 100m;
				roc.Add(Math.Round(rocValue, 2));
			}

			return roc;
		}

		// ──────────────────────────────────────────────────────────
		// 7. TRUE STRENGTH INDEX (TSI) - Double-smoothed momentum
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// True Strength Index - Double-smoothed momentum oscillator
		/// More responsive than RSI, less noisy
		/// </summary>
		public static (List<decimal> tsi, List<decimal> signal) TSI(
			List<decimal> closes, int longPeriod = 25, int shortPeriod = 13, int signalPeriod = 7)
		{
			var tsiValues = new List<decimal>();
			var signalValues = new List<decimal>();

			if (closes.Count < longPeriod + shortPeriod)
				return (tsiValues, signalValues);

			// Calculate price changes
			var priceChanges = new List<decimal>();
			for (int i = 1; i < closes.Count; i++)
			{
				priceChanges.Add(closes[i] - closes[i - 1]);
			}

			// Calculate absolute price changes
			var absChanges = priceChanges.Select(Math.Abs).ToList();

			// First EMA smoothing
			var pcEma1 = Indicators.EMAList(priceChanges, longPeriod);
			var absEma1 = Indicators.EMAList(absChanges, longPeriod);

			// Second EMA smoothing
			var pcEma2 = Indicators.EMAList(pcEma1, shortPeriod);
			var absEma2 = Indicators.EMAList(absEma1, shortPeriod);

			// Calculate TSI
			for (int i = 0; i < pcEma2.Count; i++)
			{
				decimal denom = Math.Max(absEma2[i], 1e-8m);
				decimal tsi = 100m * pcEma2[i] / denom;
				tsiValues.Add(Math.Round(tsi, 2));
			}

			// Calculate signal line
			if (tsiValues.Count >= signalPeriod)
			{
				signalValues = Indicators.EMAList(tsiValues, signalPeriod);
			}

			return (tsiValues, signalValues);
		}

		// ──────────────────────────────────────────────────────────
		// 8. ULTIMATE OSCILLATOR - Multi-timeframe momentum
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Ultimate Oscillator - Combines 3 different timeframes
		/// Reduces false signals from single-period oscillators
		/// </summary>
		public static List<decimal> UltimateOscillator(
			List<decimal> highs, List<decimal> lows, List<decimal> closes,
			int period1 = 7, int period2 = 14, int period3 = 28)
		{
			var result = new List<decimal>();
			if (closes.Count < period3 + 1) return result;

			// Calculate buying pressure and true range for each period
			var bp = new List<decimal>();  // Buying Pressure
			var tr = new List<decimal>();  // True Range

			for (int i = 1; i < closes.Count; i++)
			{
				decimal close = closes[i];
				decimal low = lows[i];
				decimal prevClose = closes[i - 1];
				decimal high = highs[i];

				bp.Add(close - Math.Min(low, prevClose));
				tr.Add(Math.Max(high, prevClose) - Math.Min(low, prevClose));
			}

			// Calculate averages for each period
			for (int i = period3 - 1; i < bp.Count; i++)
			{
				decimal avg1 = bp.Skip(i - period1 + 1).Take(period1).Sum() /
							   tr.Skip(i - period1 + 1).Take(period1).Sum();
				decimal avg2 = bp.Skip(i - period2 + 1).Take(period2).Sum() /
							   tr.Skip(i - period2 + 1).Take(period2).Sum();
				decimal avg3 = bp.Skip(i - period3 + 1).Take(period3).Sum() /
							   tr.Skip(i - period3 + 1).Take(period3).Sum();

				// Weighted average (4:2:1 ratio)
				decimal uo = 100m * ((4m * avg1) + (2m * avg2) + avg3) / 7m;
				result.Add(Math.Round(uo, 2));
			}

			return result;
		}

		// ──────────────────────────────────────────────────────────
		// 9. COMMODITY CHANNEL INDEX (CCI) - Enhanced version
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Enhanced CCI calculation with better mean deviation
		/// </summary>
		public static List<decimal> CCIEnhanced(List<decimal> highs, List<decimal> lows,
			List<decimal> closes, int period = 20)
		{
			var cci = new List<decimal>();
			if (closes.Count < period) return cci;

			// Calculate typical price
			var typicalPrice = new List<decimal>();
			for (int i = 0; i < closes.Count; i++)
			{
				typicalPrice.Add((highs[i] + lows[i] + closes[i]) / 3m);
			}

			for (int i = period - 1; i < typicalPrice.Count; i++)
			{
				var segment = typicalPrice.Skip(i - period + 1).Take(period).ToList();
				decimal sma = segment.Average();

				// Calculate mean deviation
				decimal meanDev = segment.Select(x => Math.Abs(x - sma)).Average();

				if (meanDev == 0) meanDev = 1e-8m;

				decimal cciValue = (typicalPrice[i] - sma) / (0.015m * meanDev);
				cci.Add(Math.Round(cciValue, 2));
			}

			return cci;
		}

		// ──────────────────────────────────────────────────────────
		// 10. VORTEX INDICATOR - Trend direction and strength
		// ──────────────────────────────────────────────────────────
		/// <summary>
		/// Vortex Indicator - Identifies trend reversals and strength
		/// </summary>
		public static (List<decimal> viPlus, List<decimal> viMinus) VortexIndicator(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			var viPlus = new List<decimal>();
			var viMinus = new List<decimal>();

			if (closes.Count < period + 1) return (viPlus, viMinus);

			for (int i = period; i < closes.Count; i++)
			{
				decimal sumVMPlus = 0m;
				decimal sumVMMinus = 0m;
				decimal sumTR = 0m;

				for (int j = 0; j < period; j++)
				{
					int idx = i - period + j + 1;

					// Vortex Movement
					sumVMPlus += Math.Abs(highs[idx] - lows[idx - 1]);
					sumVMMinus += Math.Abs(lows[idx] - highs[idx - 1]);

					// True Range
					decimal tr = Math.Max(
						highs[idx] - lows[idx],
						Math.Max(
							Math.Abs(highs[idx] - closes[idx - 1]),
							Math.Abs(lows[idx] - closes[idx - 1])
						)
					);
					sumTR += tr;
				}

				decimal viPlusValue = sumTR > 0 ? sumVMPlus / sumTR : 0m;
				decimal viMinusValue = sumTR > 0 ? sumVMMinus / sumTR : 0m;

				viPlus.Add(Math.Round(viPlusValue, 4));
				viMinus.Add(Math.Round(viMinusValue, 4));
			}

			return (viPlus, viMinus);
		}


		// ═══════════════════════════════════════════════════════════════════
		// NEW SWING TRADING INDICATORS
		// Add these methods to your Indicators.cs class
		// ═══════════════════════════════════════════════════════════════════

		/// <summary>
		/// Elder Ray Index - Measures buying and selling pressure
		/// Bull Power = High - EMA(13)
		/// Bear Power = Low - EMA(13)
		/// </summary>
		public static (List<decimal> bullPower, List<decimal> bearPower) ElderRay(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 13)
		{
			if (closes == null || highs == null || lows == null)
				return (new List<decimal>(), new List<decimal>());

			var ema = EMAList(closes, period);
			var bullPower = new List<decimal>();
			var bearPower = new List<decimal>();

			for (int i = 0; i < closes.Count; i++)
			{
				if (i < period - 1)
				{
					bullPower.Add(0);
					bearPower.Add(0);
				}
				else
				{
					bullPower.Add(Math.Round(highs[i] - ema[i], 4));
					bearPower.Add(Math.Round(lows[i] - ema[i], 4));
				}
			}

			return (bullPower, bearPower);
		}

		/// <summary>
		/// Choppiness Index - Determines if market is trending or ranging
		/// Values > 61.8: Market is choppy/ranging - avoid trend strategies
		/// Values < 38.2: Market is trending - use trend-following strategies
		/// Values 38.2-61.8: Neutral
		/// </summary>
		public static List<decimal> ChoppinessIndex(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
		{
			var ci = new List<decimal>();

			if (closes == null || highs == null || lows == null || closes.Count < period + 14)
				return ci;

			var atr = ATRList(highs, lows, closes, period);

			for (int i = 0; i < closes.Count; i++)
			{
				if (i < period)
				{
					ci.Add(50m); // Neutral default
					continue;
				}

				// Get ATR values for the period
				var atrWindow = atr.Skip(i - period + 1).Take(period).ToList();
				decimal atrSum = atrWindow.Sum();

				// Find highest high and lowest low in period
				var highWindow = highs.Skip(i - period + 1).Take(period);
				var lowWindow = lows.Skip(i - period + 1).Take(period);

				decimal highestHigh = highWindow.Max();
				decimal lowestLow = lowWindow.Min();
				decimal range = highestHigh - lowestLow;

				if (range == 0 || atrSum == 0)
				{
					ci.Add(50m);
					continue;
				}

				// Calculate Choppiness Index
				decimal logValue = (decimal)Math.Log10((double)(atrSum / range));
				decimal logPeriod = (decimal)Math.Log10(period);
				decimal chop = 100m * logValue / logPeriod;

				ci.Add(Math.Round(Math.Clamp(chop, 0m, 100m), 2));
			}

			return ci;
		}

		/// <summary>
		/// Linear Regression Channel - Better than Bollinger Bands for trending markets
		/// Returns: (regression line, upper channel, lower channel)
		/// </summary>
		public static (List<decimal> regression, List<decimal> upper, List<decimal> lower)
			LinearRegressionChannel(List<decimal> closes, int period = 20, decimal stdDevs = 2m)
		{
			var regression = new List<decimal>();
			var upper = new List<decimal>();
			var lower = new List<decimal>();

			if (closes == null || closes.Count < period)
				return (regression, upper, lower);

			for (int i = 0; i < closes.Count; i++)
			{
				if (i < period - 1)
				{
					regression.Add(closes[i]);
					upper.Add(closes[i]);
					lower.Add(closes[i]);
					continue;
				}

				var window = closes.Skip(i - period + 1).Take(period).ToList();

				// Calculate linear regression using least squares method
				decimal sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
				for (int j = 0; j < period; j++)
				{
					decimal x = j;
					decimal y = window[j];
					sumX += x;
					sumY += y;
					sumXY += x * y;
					sumX2 += x * x;
				}

				decimal n = period;
				decimal slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
				decimal intercept = (sumY - slope * sumX) / n;

				// Calculate regression value at current point
				decimal regValue = intercept + slope * (period - 1);

				// Calculate standard deviation from regression line
				decimal sumSquaredErrors = 0;
				for (int j = 0; j < period; j++)
				{
					decimal predictedY = intercept + slope * j;
					decimal error = window[j] - predictedY;
					sumSquaredErrors += error * error;
				}

				decimal stdDev = (decimal)Math.Sqrt((double)(sumSquaredErrors / period));

				regression.Add(Math.Round(regValue, 4));
				upper.Add(Math.Round(regValue + stdDevs * stdDev, 4));
				lower.Add(Math.Round(regValue - stdDevs * stdDev, 4));
			}

			return (regression, upper, lower);
		}

		/// <summary>
		/// Williams Alligator - Bill Williams' trend-following indicator
		/// Returns: (jaw=13-SMA, teeth=8-SMA, lips=5-SMA)
		/// Lines intertwined = sleeping alligator (no trend, avoid trading)
		/// Lines separated and parallel = eating alligator (strong trend, trade)
		/// </summary>
		public static (List<decimal> jaw, List<decimal> teeth, List<decimal> lips)
			Alligator(List<decimal> closes)
		{
			if (closes == null || closes.Count < 13)
				return (new List<decimal>(), new List<decimal>(), new List<decimal>());

			// Using SMAs instead of SMoothed MAs for simplicity
			// In production, you might want to use Smoothed Moving Averages
			var jaw = SMAList(closes, 13).Select(v => v ?? closes.FirstOrDefault()).ToList();
			var teeth = SMAList(closes, 8).Select(v => v ?? closes.FirstOrDefault()).ToList();
			var lips = SMAList(closes, 5).Select(v => v ?? closes.FirstOrDefault()).ToList();

			return (jaw, teeth, lips);
		}

		/// <summary>
		/// Pivot Points Calculator - Classic support/resistance levels
		/// Returns: (pivot, resistance1, resistance2, resistance3, support1, support2, support3)
		/// Use yesterday's high/low/close for today's pivots
		/// Weekly pivots are more reliable for swing trading
		/// </summary>
		public static (decimal pivot, decimal r1, decimal r2, decimal r3, decimal s1, decimal s2, decimal s3)
			CalculatePivotPoints(decimal high, decimal low, decimal close)
		{
			decimal pivot = (high + low + close) / 3m;

			decimal r1 = (2m * pivot) - low;
			decimal s1 = (2m * pivot) - high;

			decimal r2 = pivot + (high - low);
			decimal s2 = pivot - (high - low);

			decimal r3 = high + 2m * (pivot - low);
			decimal s3 = low - 2m * (high - pivot);

			return (
				Math.Round(pivot, 4),
				Math.Round(r1, 4), Math.Round(r2, 4), Math.Round(r3, 4),
				Math.Round(s1, 4), Math.Round(s2, 4), Math.Round(s3, 4)
			);
		}

		/// <summary>
		/// Fibonacci Retracement Levels - Common reversal zones
		/// Returns list of (fib level, price, label)
		/// Key levels: 38.2%, 50%, 61.8% (Golden Ratio)
		/// </summary>
		public static List<(decimal level, decimal price, string label)> FibonacciRetracement(
			decimal swingHigh, decimal swingLow, bool isUptrend = true)
		{
			var fibLevels = new[] { 0m, 0.236m, 0.382m, 0.5m, 0.618m, 0.786m, 1m };
			var range = swingHigh - swingLow;

			return fibLevels.Select(fib =>
			{
				decimal price = isUptrend
					? swingHigh - (range * fib)
					: swingLow + (range * fib);

				string label = fib switch
				{
					0m => "0%",
					0.236m => "23.6%",
					0.382m => "38.2%",
					0.5m => "50%",
					0.618m => "61.8%",
					0.786m => "78.6%",
					1m => "100%",
					_ => $"{fib:P1}"
				};

				return (level: fib, price: Math.Round(price, 2), label: label);
			}).ToList();
		}

		/// <summary>
		/// Heikin-Ashi Transformation - Smoothed candlesticks for easier trend identification
		/// Long HA candles = strong trend (hold position)
		/// Short HA candles with upper/lower shadows = trend weakness (prepare to exit)
		/// </summary>
		public static List<(decimal open, decimal high, decimal low, decimal close)> HeikinAshi(
			List<decimal> opens, List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			var ha = new List<(decimal open, decimal high, decimal low, decimal close)>();

			if (closes == null || opens == null || highs == null || lows == null)
				return ha;

			if (closes.Count != opens.Count || closes.Count != highs.Count || closes.Count != lows.Count)
				return ha;

			decimal haOpen = (opens[0] + closes[0]) / 2m;
			decimal haClose = (opens[0] + highs[0] + lows[0] + closes[0]) / 4m;

			for (int i = 0; i < closes.Count; i++)
			{
				if (i > 0)
				{
					haOpen = (ha[i - 1].open + ha[i - 1].close) / 2m;
				}

				haClose = (opens[i] + highs[i] + lows[i] + closes[i]) / 4m;
				decimal haHigh = Math.Max(highs[i], Math.Max(haOpen, haClose));
				decimal haLow = Math.Min(lows[i], Math.Min(haOpen, haClose));

				ha.Add((
					Math.Round(haOpen, 4),
					Math.Round(haHigh, 4),
					Math.Round(haLow, 4),
					Math.Round(haClose, 4)
				));
			}

			return ha;
		}

		/// <summary>
		/// ADXR (Average Directional Movement Index Rating)
		/// Smoother version of ADX - more reliable for swing trading
		/// ADXR = (Current ADX + ADX from N periods ago) / 2
		/// </summary>
		public static List<decimal> ADXR(List<decimal> adxValues, int period = 14)
		{
			var adxr = new List<decimal>();

			if (adxValues == null || adxValues.Count < period)
				return adxr;

			for (int i = 0; i < adxValues.Count; i++)
			{
				if (i < period)
				{
					adxr.Add(adxValues[i]); // Use ADX value for initial periods
				}
				else
				{
					decimal currentADX = adxValues[i];
					decimal pastADX = adxValues[i - period];
					decimal adxrValue = (currentADX + pastADX) / 2m;
					adxr.Add(Math.Round(adxrValue, 2));
				}
			}

			return adxr;
		}

		/// <summary>
		/// Detect Swing Highs - Used for pattern recognition and Fibonacci calculations
		/// A swing high is a bar with high > highs of N bars before and after
		/// </summary>
		public static List<(int index, decimal price)> FindSwingHighs(
			List<decimal> highs, int lookback = 5)
		{
			var swingHighs = new List<(int index, decimal price)>();

			if (highs == null || highs.Count < lookback * 2 + 1)
				return swingHighs;

			for (int i = lookback; i < highs.Count - lookback; i++)
			{
				bool isSwingHigh = true;
				decimal currentHigh = highs[i];

				// Check bars before
				for (int j = i - lookback; j < i; j++)
				{
					if (highs[j] >= currentHigh)
					{
						isSwingHigh = false;
						break;
					}
				}

				// Check bars after
				if (isSwingHigh)
				{
					for (int j = i + 1; j <= i + lookback; j++)
					{
						if (highs[j] >= currentHigh)
						{
							isSwingHigh = false;
							break;
						}
					}
				}

				if (isSwingHigh)
				{
					swingHighs.Add((i, currentHigh));
				}
			}

			return swingHighs;
		}

		/// <summary>
		/// Detect Swing Lows - Used for pattern recognition and Fibonacci calculations
		/// A swing low is a bar with low < lows of N bars before and after
		/// </summary>
		public static List<(int index, decimal price)> FindSwingLows(
			List<decimal> lows, int lookback = 5)
		{
			var swingLows = new List<(int index, decimal price)>();

			if (lows == null || lows.Count < lookback * 2 + 1)
				return swingLows;

			for (int i = lookback; i < lows.Count - lookback; i++)
			{
				bool isSwingLow = true;
				decimal currentLow = lows[i];

				// Check bars before
				for (int j = i - lookback; j < i; j++)
				{
					if (lows[j] <= currentLow)
					{
						isSwingLow = false;
						break;
					}
				}

				// Check bars after
				if (isSwingLow)
				{
					for (int j = i + 1; j <= i + lookback; j++)
					{
						if (lows[j] <= currentLow)
						{
							isSwingLow = false;
							break;
						}
					}
				}

				if (isSwingLow)
				{
					swingLows.Add((i, currentLow));
				}
			}

			return swingLows;
		}

		/// <summary>
		/// Aggregate Daily Bars to Weekly Bars
		/// Essential for weekly trend analysis in swing trading
		/// </summary>
		public static List<(decimal open, decimal high, decimal low, decimal close, decimal volume, DateTime timestamp)>
			AggregateToWeekly(
				List<decimal> opens, List<decimal> highs, List<decimal> lows,
				List<decimal> closes, List<decimal> volumes, List<DateTime> timestamps)
		{
			var weeklyBars = new List<(decimal open, decimal high, decimal low, decimal close, decimal volume, DateTime timestamp)>();

			if (closes == null || closes.Count < 5)
				return weeklyBars;

			for (int i = 0; i + 4 < closes.Count; i += 5)
			{
				int endIdx = Math.Min(i + 4, closes.Count - 1);
				int barsInWeek = endIdx - i + 1;

				if (barsInWeek < 5) break; // Skip incomplete weeks

				decimal weekOpen = opens[i];
				decimal weekHigh = highs.Skip(i).Take(barsInWeek).Max();
				decimal weekLow = lows.Skip(i).Take(barsInWeek).Min();
				decimal weekClose = closes[endIdx];
				decimal weekVolume = volumes.Skip(i).Take(barsInWeek).Sum();
				DateTime weekEnd = timestamps[endIdx];

				weeklyBars.Add((weekOpen, weekHigh, weekLow, weekClose, weekVolume, weekEnd));
			}

			return weeklyBars;
		}

	}



	// ═══════════════════════════════════════════════════════════════
	// SIGNAL VALIDATION FRAMEWORK
	// ═══════════════════════════════════════════════════════════════
	public static class SignalValidator
	{
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
			public string Description { get; set; } = "";

		}
		public record SupportResistanceLevel
		{
			public decimal Level { get; set; }
			public bool IsSupport { get; set; }
			public int TouchCount { get; set; }
			public decimal Strength { get; set; }
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

		/// <summary>Market context analysis</summary>
		/// <summary>
		/// SWING TRADING: Analyze market context with longer lookback
		/// </summary>
		public static MarketContext AnalyzeMarketContext(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, int idx)
		{
			if (idx < 30) // ⭐ SWING: Need 30+ days
				return new MarketContext { Description = "Insufficient data" };

			// ⭐ SWING: Use 20/50 EMAs for trend (not 9/21)
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			if (ema20.Count <= idx || ema50.Count <= idx)
				return new MarketContext { Description = "EMA not ready" };

			decimal currentPrice = closes[idx];
			decimal ema20Val = ema20[idx];
			decimal ema50Val = ema50[idx];

			// Trend determination
			bool priceAboveEma20 = currentPrice > ema20Val;
			bool priceAboveEma50 = currentPrice > ema50Val;
			bool ema20AboveEma50 = ema20Val > ema50Val;

			// ⭐ SWING: Calculate trend strength over 10 days (not 5)
			decimal trendStrength = 0m;
			if (idx >= 10)
			{
				var recentPrices = closes.Skip(idx - 10).Take(10).ToList();
				var avgChange = recentPrices.Zip(recentPrices.Skip(1), (a, b) => (b - a) / a).Average();
				trendStrength = Math.Abs(avgChange);
			}

			// Determine trend
			bool isUptrend = priceAboveEma20 && priceAboveEma50 && ema20AboveEma50;
			bool isDowntrend = !priceAboveEma20 && !priceAboveEma50 && !ema20AboveEma50;

			string description = isUptrend ? "Strong Uptrend" :
								isDowntrend ? "Strong Downtrend" :
								priceAboveEma20 ? "Weak Uptrend" : "Weak Downtrend";

			return new MarketContext
			{
				IsUptrend = isUptrend,
				IsDowntrend = isDowntrend,
				TrendStrength = trendStrength,
				Description = description
			};
		}

		/// <summary>
		/// SWING TRADING: EMA crossover with longer periods
		/// </summary>
		public static SignalValidation ValidateEMACrossover(
			List<decimal> closes, List<decimal> emaFast, List<decimal> emaSlow,
			int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || emaFast.Count <= idx || emaSlow.Count <= idx)
				return validation.Fail("Insufficient EMA data");

			decimal priceCurrent = closes[idx];
			decimal emaF = emaFast[idx];
			decimal emaS = emaSlow[idx];

			// ⭐ SWING: Need meaningful separation (0.8% instead of 0.3%)
			decimal separation = Math.Abs(emaF - emaS) / priceCurrent;
			if (separation < 0.008m)
				return validation.Fail($"EMAs too close ({separation:P2})");

			bool isBuy = direction == "Buy";

			// ⭐ SWING: Check momentum over 3 days (not 2)
			bool momentum = idx >= 3 &&
				(isBuy ? closes[idx] > closes[idx - 3] : closes[idx] < closes[idx - 3]);

			if (!momentum)
				return validation.Fail("Weak price momentum");

			validation.IsValid = true;
			validation.Confidence = 0.60m + Math.Min(separation * 30m, 0.25m);
			validation.Reason = $"EMA crossover validated (sep={separation:P2})";

			return validation;
		}


		/// <summary>RSI signal validation</summary>
		/// <summary>
		/// SWING TRADING: RSI validation with swing-appropriate levels
		/// </summary>
		public static SignalValidation ValidateRSI(List<decimal> rsi, List<decimal> prices, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (rsi.Count == 0 || idx >= rsi.Count)
				return validation.Fail("RSI not available");

			decimal rsiValue = rsi[idx];
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// ⭐ SWING: More lenient RSI for swing trading
				if (rsiValue < 30m)  // Oversold - excellent
				{
					validation.IsValid = true;
					validation.Confidence = 0.85m;
					validation.Reason = $"RSI oversold ({rsiValue:F1})";
				}
				else if (rsiValue >= 30m && rsiValue <= 55m)  // ⭐ SWING: Good range
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"RSI favorable ({rsiValue:F1})";
				}
				else if (rsiValue > 55m && rsiValue <= 65m)  // Acceptable
				{
					validation.IsValid = true;
					validation.Confidence = 0.55m;
					validation.Reason = $"RSI acceptable ({rsiValue:F1})";
				}
				else
				{
					return validation.Fail($"RSI too high for buy ({rsiValue:F1})");
				}
			}
			else // Sell
			{
				if (rsiValue > 70m)  // Overbought - excellent
				{
					validation.IsValid = true;
					validation.Confidence = 0.85m;
					validation.Reason = $"RSI overbought ({rsiValue:F1})";
				}
				else if (rsiValue >= 45m && rsiValue <= 70m)  // ⭐ SWING: Good range
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"RSI favorable ({rsiValue:F1})";
				}
				else if (rsiValue >= 35m && rsiValue < 45m)  // Acceptable
				{
					validation.IsValid = true;
					validation.Confidence = 0.55m;
					validation.Reason = $"RSI acceptable ({rsiValue:F1})";
				}
				else
				{
					return validation.Fail($"RSI too low for sell ({rsiValue:F1})");
				}
			}

			return validation;
		}

		/// <summary>MACD signal validation</summary>
		public static SignalValidation ValidateMACD(List<decimal> macdHist, List<decimal> prices,
			List<decimal> macdLine, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || macdHist.Count <= idx)
				return validation.Fail("Insufficient MACD data");

			bool isBuy = direction == "Buy";

			bool crossover = isBuy
				? (macdHist[idx] > 0 && macdHist[idx - 1] <= 0)
				: (macdHist[idx] < 0 && macdHist[idx - 1] >= 0);

			if (!crossover)
				return validation.Fail("No MACD crossover");

			decimal pricePercent = Math.Abs(prices[idx]) * 0.0015m;
			if (Math.Abs(macdHist[idx]) < pricePercent)
				return validation.Fail($"Histogram too weak: {macdHist[idx]:F4}");

			bool histMomentum = isBuy
				? (macdHist[idx] > macdHist[idx - 1])
				: (macdHist[idx] < macdHist[idx - 1]);

			if (!histMomentum)
				return validation.Fail("Weak histogram momentum");

			bool recentWhipsaw = idx >= 4 && (isBuy
				? (macdHist[idx - 3] < 0 && macdHist[idx - 4] >= 0)
				: (macdHist[idx - 3] > 0 && macdHist[idx - 4] <= 0));

			if (recentWhipsaw)
				return validation.Fail("Recent whipsaw detected");

			bool macdTrend = isBuy
				? (macdLine[idx] > macdLine[idx - 3])
				: (macdLine[idx] < macdLine[idx - 3]);

			validation.IsValid = true;
			validation.Confidence = macdTrend ? 0.85m : 0.65m;
			validation.Reason = $"Valid MACD {direction} (hist={macdHist[idx]:F4})";

			return validation;
		}

		/// <summary>StochRSI validation</summary>
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

			if (kNow > 0.42m && kNow < 0.58m)  // ⚖️ BALANCED: Wider neutral zone
				return validation.Fail("StochRSI in neutral zone");

			if (isBuy)
			{
				bool wasOversold = stochK.Skip(Math.Max(0, idx - 10)).Take(10).Any(k => k < 0.40m);  // ⚖️ MORE LENIENT: <0.30
				if (!wasOversold)
					return validation.Fail("No oversold condition");

				bool crossover = kNow > dNow && kPrev <= stochD[idx - 1];
				if (!crossover)
					return validation.Fail("No bullish crossover");

				bool rsiConfirm = rsi.Count > idx && rsi[idx] > 40m && rsi[idx] < 70m;

				validation.IsValid = true;
				validation.Confidence = rsiConfirm ? 0.8m : 0.65m;
				validation.Reason = $"StochRSI buy (K={kNow:F2}, D={dNow:F2})";
			}
			else
			{
				bool wasOverbought = stochK.Skip(Math.Max(0, idx - 10)).Take(10).Any(k => k > 0.60m);  // ⚖️ MORE LENIENT: >0.70
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

		/// <summary>
		/// SWING TRADING: Volume spike validation with daily perspective
		/// </summary>
		public static SignalValidation ValidateVolumeSpike(List<decimal> volumes, List<decimal> prices,
			int idx, decimal spikeMultiple = 1.3m)  // ⭐ SWING: 1.3x threshold
		{
			var validation = new SignalValidation { IsValid = false };

			if (volumes == null || volumes.Count <= idx || idx < 30)  // ⭐ SWING: 30-day average
				return validation.Fail("Insufficient volume data");

			decimal currentVol = volumes[idx];
			var recentVols = volumes.Skip(Math.Max(0, idx - 30)).Take(30).ToList();
			decimal avgVol = recentVols.Average();

			if (avgVol <= 0)
				return validation.Fail("Invalid volume baseline");

			decimal volMultiple = currentVol / avgVol;
			if (volMultiple < spikeMultiple)
				return validation.Fail($"No volume spike: {volMultiple:F2}x");

			bool upBar = prices[idx] > prices[idx - 1];

			validation.IsValid = true;
			validation.Confidence = Math.Min((volMultiple - spikeMultiple) / spikeMultiple + 0.65m, 1m);
			validation.Reason = $"Volume spike {volMultiple:F2}x on {(upBar ? "up" : "down")} bar";

			return validation;
		}

		/// <summary>CCI validation</summary>
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
				if (!(cciPrev <= -60m && cciNow > -60m))  // ⚖️ MORE LENIENT: -70
					return validation.Fail("No CCI oversold recovery (-80)");

				bool momentum = cciNow > cciPrev && cci[idx - 1] > cci[idx - 2];

				validation.IsValid = true;
				validation.Confidence = momentum ? 0.75m : 0.6m;
				validation.Reason = $"CCI buy signal ({cciPrev:F0}→{cciNow:F0})";
			}
			else
			{
				if (!(cciPrev >= 60m && cciNow < 60m))  // ⚖️ MORE LENIENT: 70
					return validation.Fail("No CCI overbought reversal (80)");

				bool momentum = cciNow < cciPrev && cci[idx - 1] < cci[idx - 2];

				validation.IsValid = true;
				validation.Confidence = momentum ? 0.75m : 0.6m;
				validation.Reason = $"CCI sell signal ({cciPrev:F0}→{cciNow:F0})";
			}

			return validation;
		}

		/// <summary>Donchian breakout validation</summary>
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

			decimal vol = atr.Count > idx && price > 0 ? atr[idx] / price : 0m;
			if (vol < 0.0012m)  // ⚖️ MORE LENIENT: 0.15% (lowered from 0.2%)
				return validation.Fail($"Insufficient volatility: {vol:P2}");

			if (isBuy)
			{
				if (price <= u)
					return validation.Fail("No breakout above upper band");

				if (prices[idx - 1] > u)
					return validation.Fail("No clear breakout (already above)");

				bool momentum = prices[idx] > prices[idx - 2];
				if (!momentum)
					return validation.Fail("Weak momentum");

				decimal breakoutStrength = (price - u) / Math.Max(price * 0.02m, 1e-8m);
				validation.IsValid = true;
				validation.Confidence = Math.Min(breakoutStrength * 2m + 0.5m, 1m);
				validation.Reason = $"Donchian breakout ↑ (${price:F2} > ${u:F2})";
			}
			else
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

		/// <summary>
		/// SWING TRADING: Support/Resistance finder with daily bars
		/// </summary>
		public static List<SupportResistanceLevel> FindSupportResistance(
			List<decimal> highs, List<decimal> lows, List<decimal> closes,
			int lookback = 60)  // ⭐ SWING: 60 days lookback
		{
			var levels = new List<SupportResistanceLevel>();

			if (closes.Count < lookback + 10)
				return levels;

			int endIdx = closes.Count - 1;
			int startIdx = Math.Max(0, endIdx - lookback);

			// ⭐ SWING: Find swing highs/lows with 3-day window
			var swingHighs = new List<(int idx, decimal price)>();
			var swingLows = new List<(int idx, decimal price)>();

			for (int i = startIdx + 3; i <= endIdx - 3; i++)
			{
				// Swing high: higher than 3 days on each side
				if (highs[i] > highs[i - 1] && highs[i] > highs[i - 2] && highs[i] > highs[i - 3] &&
					highs[i] > highs[i + 1] && highs[i] > highs[i + 2] && highs[i] > highs[i + 3])
				{
					swingHighs.Add((i, highs[i]));
				}

				// Swing low: lower than 3 days on each side
				if (lows[i] < lows[i - 1] && lows[i] < lows[i - 2] && lows[i] < lows[i - 3] &&
					lows[i] < lows[i + 1] && lows[i] < lows[i + 2] && lows[i] < lows[i + 3])
				{
					swingLows.Add((i, lows[i]));
				}
			}

			// Cluster levels - ⭐ SWING: 1.5% proximity (not 1%)
			decimal clusterTolerance = 0.015m;

			// Process resistance levels
			var resistanceClusters = ClusterLevels(swingHighs.Select(s => s.price).ToList(), clusterTolerance);
			foreach (var level in resistanceClusters.Take(5))
			{
				int touches = swingHighs.Count(s => Math.Abs(s.price - level) / level < clusterTolerance);
				levels.Add(new SupportResistanceLevel
				{
					Level = level,
					IsSupport = false,
					TouchCount = touches,
					Strength = Math.Min(touches / 3m, 1m)
				});
			}

			// Process support levels
			var supportClusters = ClusterLevels(swingLows.Select(s => s.price).ToList(), clusterTolerance);
			foreach (var level in supportClusters.Take(5))
			{
				int touches = swingLows.Count(s => Math.Abs(s.price - level) / level < clusterTolerance);
				levels.Add(new SupportResistanceLevel
				{
					Level = level,
					IsSupport = true,
					TouchCount = touches,
					Strength = Math.Min(touches / 3m, 1m)
				});
			}

			return levels.OrderByDescending(l => l.Strength).ToList();
		}

		private static List<decimal> ClusterLevels(List<decimal> levels, decimal tolerance)
		{
			if (levels.Count == 0) return new List<decimal>();

			var clusters = new List<List<decimal>>();
			var sorted = levels.OrderBy(l => l).ToList();

			var currentCluster = new List<decimal> { sorted[0] };

			for (int i = 1; i < sorted.Count; i++)
			{
				if (Math.Abs(sorted[i] - currentCluster.Last()) / sorted[i] < tolerance)
				{
					currentCluster.Add(sorted[i]);
				}
				else
				{
					clusters.Add(currentCluster);
					currentCluster = new List<decimal> { sorted[i] };
				}
			}
			clusters.Add(currentCluster);

			return clusters
				.OrderByDescending(c => c.Count)
				.Select(c => c.Average())
				.ToList();
		}
		// ──────────────────────────────────────────────────────────
		// PRIVATE HELPERS
		// ──────────────────────────────────────────────────────────

		private static decimal CalculateVolatility(List<decimal> prices)
		{
			if (prices.Count < 2) return 0m;

			decimal avg = prices.Average();
			decimal sumSquares = prices.Sum(p => (p - avg) * (p - avg));
			return (decimal)Math.Sqrt((double)(sumSquares / prices.Count));
		}

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

		private static decimal CalculateConfidence(decimal separation, bool momentum,
			bool priceConfirm, bool trendAligned)
		{
			decimal conf = 0.3m;
			conf += Math.Min(separation * 50m, 0.3m);
			if (momentum) conf += 0.2m;
			if (priceConfirm) conf += 0.15m;
			if (trendAligned) conf += 0.05m;
			return Math.Min(conf, 1m);
		}


		/// <summary>
		/// PRIMARY FILTER #1: Market must be trending
		/// Call this BEFORE any other validation
		/// BALANCED: ADX >= 20 for trending signals
		/// </summary>
		public static SignalValidation ValidateTrendStrength(
			List<decimal> adx, List<decimal> diPlus, List<decimal> diMinus,
			int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 28 || adx.Count <= idx)
				return validation.Fail("Insufficient ADX data");

			decimal adxValue = adx[idx];
			bool isBuy = direction == "Buy";

			// GATE: Market must be trending (BALANCED: ADX >= 20)
			if (adxValue < 20m)
				return validation.Fail($"Market not trending (ADX={adxValue:F1} < 20)");

			// Check DI alignment with direction
			if (diPlus.Count > idx && diMinus.Count > idx)
			{
				decimal diPlusVal = diPlus[idx];
				decimal diMinusVal = diMinus[idx];

				if (isBuy && diPlusVal < diMinusVal)
					return validation.Fail($"DI- dominant ({diMinusVal:F1} > {diPlusVal:F1}) - bearish");

				if (!isBuy && diMinusVal < diPlusVal)
					return validation.Fail($"DI+ dominant ({diPlusVal:F1} > {diMinusVal:F1}) - bullish");
			}

			// Confidence based on ADX strength (BALANCED tiers)
			if (adxValue >= 30m)
			{
				validation.IsValid = true;
				validation.Confidence = 0.85m;
				validation.Reason = $"Strong trend (ADX={adxValue:F1})";
			}
			else if (adxValue >= 25m)
			{
				validation.IsValid = true;
				validation.Confidence = 0.75m;
				validation.Reason = $"Moderate trend (ADX={adxValue:F1})";
			}
			else // 20-25
			{
				validation.IsValid = true;
				validation.Confidence = 0.60m;
				validation.Reason = $"Weak trend (ADX={adxValue:F1})";
			}

			return validation;
		}

		/// <summary>
		/// PRIMARY FILTER #2: Market must not be choppy
		/// Call this BEFORE any other validation
		/// </summary>
		public static SignalValidation ValidateChoppinessFilter(List<decimal> ci, int idx)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 14 || ci.Count <= idx)
				return validation.Fail("Insufficient Choppiness data");

			decimal ciValue = ci[idx];

			// GATE: Market must not be too choppy (relaxed from 61.8 to 65)
			if (ciValue > 65m)
				return validation.Fail($"Market too choppy (CI={ciValue:F1} > 65)");

			if (ciValue < 38.2m)
			{
				validation.IsValid = true;
				validation.Confidence = 0.90m;
				validation.Reason = $"Strong directional market (CI={ciValue:F1})";
			}
			else if (ciValue <= 50m)
			{
				validation.IsValid = true;
				validation.Confidence = 0.75m;
				validation.Reason = $"Moderate trend (CI={ciValue:F1})";
			}
			else // 50 - 61.8
			{
				validation.IsValid = true;
				validation.Confidence = 0.55m;
				validation.Reason = $"Borderline choppy (CI={ciValue:F1})";
			}

			return validation;
		}

		/// <summary>
		/// PRIMARY FILTER #3: Signal must align with longer-term trend
		/// </summary>
		public static SignalValidation ValidateTrendAlignment(
			List<decimal> closes, List<decimal> ema50, List<decimal> ema200,
			int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 200 || ema50.Count <= idx || ema200.Count <= idx)
			{
				// Not enough data for 200 EMA, use 50-day trend
				if (idx >= 50 && closes.Count > idx)
				{
					decimal change50 = (closes[idx] - closes[idx - 50]) / closes[idx - 50];
					bool isBuy = direction == "Buy";

					if (isBuy && change50 < -0.10m)
						return validation.Fail($"Strong downtrend ({change50:P1}) - avoid buy");
					if (!isBuy && change50 > 0.10m)
						return validation.Fail($"Strong uptrend ({change50:P1}) - avoid sell");

					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"50-day trend acceptable ({change50:P1})";
					return validation;
				}

				validation.IsValid = true;
				validation.Confidence = 0.50m;
				validation.Reason = "Insufficient trend data - proceed with caution";
				return validation;
			}

			bool isBuyDir = direction == "Buy";
			decimal price = closes[idx];
			decimal ema50Val = ema50[idx];
			decimal ema200Val = ema200[idx];

			// Perfect alignment
			bool bullishAlign = price > ema50Val && ema50Val > ema200Val;
			bool bearishAlign = price < ema50Val && ema50Val < ema200Val;

			if (isBuyDir)
			{
				if (bullishAlign)
				{
					validation.IsValid = true;
					validation.Confidence = 0.90m;
					validation.Reason = "Perfect bullish alignment (P > 50 > 200)";
				}
				else if (price > ema200Val)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = "Price above 200 EMA - acceptable";
				}
				else
				{
					return validation.Fail("Price below 200 EMA - avoid buy");
				}
			}
			else // Sell
			{
				if (bearishAlign)
				{
					validation.IsValid = true;
					validation.Confidence = 0.90m;
					validation.Reason = "Perfect bearish alignment (P < 50 < 200)";
				}
				else if (price < ema200Val)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = "Price below 200 EMA - acceptable";
				}
				else
				{
					return validation.Fail("Price above 200 EMA - avoid sell");
				}
			}

			return validation;
		}


		#region ENHANCED ENTRY SIGNAL VALIDATIONS

		/// <summary>
		/// Enhanced RSI with Divergence Detection
		/// Higher win rate than standard RSI
		/// </summary>
		public static SignalValidation ValidateRSIWithDivergence(
			List<decimal> rsi, List<decimal> prices, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (rsi.Count == 0 || idx >= rsi.Count || idx < 14)
				return validation.Fail("RSI not available");

			decimal rsiValue = rsi[idx];
			bool isBuy = direction == "Buy";

			// Check for divergence (HIGH WIN RATE SIGNAL)
			bool hasDivergence = DetectRSIDivergence(prices, rsi, idx, isBuy);

			if (isBuy)
			{
				if (rsiValue < 30m)
				{
					validation.IsValid = true;
					validation.Confidence = hasDivergence ? 0.92m : 0.82m;
					validation.Reason = hasDivergence
						? $"RSI oversold + bullish divergence ({rsiValue:F1})"
						: $"RSI oversold ({rsiValue:F1})";
				}
				else if (rsiValue < 40m && hasDivergence)
				{
					validation.IsValid = true;
					validation.Confidence = 0.78m;
					validation.Reason = $"RSI bullish divergence ({rsiValue:F1})";
				}
				else if (rsiValue >= 30m && rsiValue <= 55m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.65m;
					validation.Reason = $"RSI favorable ({rsiValue:F1})";
				}
				else
				{
					return validation.Fail($"RSI too high for buy ({rsiValue:F1})");
				}
			}
			else // Sell
			{
				if (rsiValue > 70m)
				{
					validation.IsValid = true;
					validation.Confidence = hasDivergence ? 0.92m : 0.82m;
					validation.Reason = hasDivergence
						? $"RSI overbought + bearish divergence ({rsiValue:F1})"
						: $"RSI overbought ({rsiValue:F1})";
				}
				else if (rsiValue > 60m && hasDivergence)
				{
					validation.IsValid = true;
					validation.Confidence = 0.78m;
					validation.Reason = $"RSI bearish divergence ({rsiValue:F1})";
				}
				else if (rsiValue >= 45m && rsiValue <= 70m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.65m;
					validation.Reason = $"RSI favorable ({rsiValue:F1})";
				}
				else
				{
					return validation.Fail($"RSI too low for sell ({rsiValue:F1})");
				}
			}

			return validation;
		}

		private static bool DetectRSIDivergence(List<decimal> prices, List<decimal> rsi, int idx, bool bullish)
		{
			if (idx < 14 || rsi.Count <= idx) return false;

			int lookback = 14;
			int startIdx = Math.Max(0, idx - lookback);
			int actualLookback = idx - startIdx;

			if (actualLookback < 10) return false;

			var recentPrices = prices.Skip(startIdx).Take(actualLookback + 1).ToList();
			var recentRSI = rsi.Skip(startIdx).Take(actualLookback + 1).ToList();

			if (recentPrices.Count != recentRSI.Count || recentPrices.Count < 10)
				return false;

			if (bullish)
			{
				// Find two significant lows in price
				int currentLowIdx = recentPrices.Count - 1;
				decimal currentLow = recentPrices[currentLowIdx];

				// Find previous low in first half
				var firstHalf = recentPrices.Take(recentPrices.Count / 2).ToList();
				if (firstHalf.Count == 0) return false;

				int prevLowIdx = firstHalf.IndexOf(firstHalf.Min());
				decimal prevLow = firstHalf[prevLowIdx];

				// Bullish divergence: Price lower low, RSI higher low
				if (currentLow < prevLow &&
					recentRSI[currentLowIdx] > recentRSI[prevLowIdx] + 3m) // Need meaningful RSI increase
				{
					return true;
				}
			}
			else
			{
				// Find two significant highs in price
				int currentHighIdx = recentPrices.Count - 1;
				decimal currentHigh = recentPrices[currentHighIdx];

				var firstHalf = recentPrices.Take(recentPrices.Count / 2).ToList();
				if (firstHalf.Count == 0) return false;

				int prevHighIdx = firstHalf.IndexOf(firstHalf.Max());
				decimal prevHigh = firstHalf[prevHighIdx];

				// Bearish divergence: Price higher high, RSI lower high
				if (currentHigh > prevHigh &&
					recentRSI[currentHighIdx] < recentRSI[prevHighIdx] - 3m)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Enhanced MACD with histogram momentum analysis
		/// </summary>
		public static SignalValidation ValidateMACDEnhanced(
			List<decimal> macdLine, List<decimal> signalLine, List<decimal> histogram,
			List<decimal> prices, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 10 || histogram.Count <= idx || macdLine.Count <= idx)
				return validation.Fail("Insufficient MACD data");

			bool isBuy = direction == "Buy";
			decimal histNow = histogram[idx];
			decimal histPrev = histogram[idx - 1];
			decimal histPrev2 = histogram[idx - 2];
			decimal macdNow = macdLine[idx];
			decimal macdPrev = macdLine[idx - 1];

			// Check for zero-line crossover (strong signal)
			bool zeroCrossover = isBuy
				? (histNow > 0 && histPrev <= 0)
				: (histNow < 0 && histPrev >= 0);

			// Check for momentum (histogram expanding)
			bool histMomentum = isBuy
				? (histNow > histPrev && histPrev > histPrev2)
				: (histNow < histPrev && histPrev < histPrev2);

			// Check MACD line momentum
			bool macdMomentum = isBuy
				? (macdNow > macdPrev)
				: (macdNow < macdPrev);

			// Minimum histogram strength (0.1% of price)
			decimal minHistStrength = prices[idx] * 0.001m;
			bool hasStrength = Math.Abs(histNow) > minHistStrength;

			if (!hasStrength)
				return validation.Fail($"Histogram too weak ({Math.Abs(histNow):F4} < {minHistStrength:F4})");

			// Check for whipsaw (recent opposite crossover)
			bool recentWhipsaw = false;
			for (int i = idx - 5; i >= Math.Max(0, idx - 10); i--)
			{
				if (histogram.Count <= i + 1) continue;
				bool oppCross = isBuy
					? (histogram[i] < 0 && histogram[i + 1] > 0)
					: (histogram[i] > 0 && histogram[i + 1] < 0);
				if (oppCross) { recentWhipsaw = true; break; }
			}

			if (recentWhipsaw)
				return validation.Fail("Recent whipsaw detected - avoid");

			// Score the signal
			if (zeroCrossover && histMomentum && macdMomentum)
			{
				validation.IsValid = true;
				validation.Confidence = 0.88m;
				validation.Reason = $"MACD crossover + momentum (hist={histNow:F4})";
			}
			else if (zeroCrossover && histMomentum)
			{
				validation.IsValid = true;
				validation.Confidence = 0.75m;
				validation.Reason = $"MACD crossover (hist={histNow:F4})";
			}
			else if (histMomentum && macdMomentum && hasStrength)
			{
				validation.IsValid = true;
				validation.Confidence = 0.65m;
				validation.Reason = $"MACD momentum building (hist={histNow:F4})";
			}
			else
			{
				return validation.Fail("No clear MACD signal");
			}

			return validation;
		}

		/// <summary>
		/// Supertrend validation optimized for swing trading
		/// </summary>
		public static SignalValidation ValidateSupertrendSwing(
			List<decimal> supertrend, List<int> direction, List<decimal> closes,
			List<decimal> atr, int idx)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || direction.Count <= idx || supertrend.Count <= idx)
				return validation.Fail("Insufficient Supertrend data");

			int currentDir = direction[idx];
			int prevDir = direction[idx - 1];
			decimal price = closes[idx];
			decimal stValue = supertrend[idx];

			// Check for direction flip (primary signal)
			bool directionFlip = currentDir != prevDir;

			if (!directionFlip)
			{
				// Check for strong continuation (price well above/below supertrend)
				decimal stDistance = Math.Abs(price - stValue) / price;

				if (currentDir == 1 && stDistance > 0.02m)
				{
					validation.IsValid = true;
					// ⭐ Gradual confidence reduction as extension increases (NO REJECTION)
					decimal extensionPenalty = stDistance > 0.08m ? (stDistance - 0.08m) * 0.8m : 0m;
					validation.Confidence = Math.Max(0.45m, 0.65m - extensionPenalty);
					validation.Reason = $"Supertrend bullish continuation ({stDistance:P2} above)";
				}
				else if (currentDir == -1 && stDistance > 0.02m)
				{
					validation.IsValid = true;
					// ⭐ Gradual confidence reduction as extension increases (NO REJECTION)
					decimal extensionPenalty = stDistance > 0.08m ? (stDistance - 0.08m) * 0.8m : 0m;
					validation.Confidence = Math.Max(0.45m, 0.65m - extensionPenalty);
					validation.Reason = $"Supertrend bearish continuation ({stDistance:P2} below)";
				}
				else
				{
					return validation.Fail("No Supertrend signal");
				}

				return validation;
			}

			// Direction flip detected
			if (currentDir == 1)
			{
				// Bullish flip - check it's not a false breakout
				decimal atrValue = atr.Count > idx ? atr[idx] : 0;
				decimal breakoutStrength = atrValue > 0 ? (price - stValue) / atrValue : 0;

				if (breakoutStrength > 0.5m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.82m;
					validation.Reason = $"Supertrend bullish flip (strength={breakoutStrength:F2}x ATR)";
				}
				else
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"Supertrend bullish flip";
				}
			}
			else
			{
				// Bearish flip
				decimal atrValue = atr.Count > idx ? atr[idx] : 0;
				decimal breakdownStrength = atrValue > 0 ? (stValue - price) / atrValue : 0;

				if (breakdownStrength > 0.5m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.82m;
					validation.Reason = $"Supertrend bearish flip (strength={breakdownStrength:F2}x ATR)";
				}
				else
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"Supertrend bearish flip";
				}
			}

			return validation;
		}

		/// <summary>
		/// Improved CCI validation with ±100 thresholds
		/// </summary>
		public static SignalValidation ValidateCCISwing(List<decimal> cci, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 5 || cci.Count <= idx)
				return validation.Fail("Insufficient CCI data");

			decimal cciNow = cci[idx];
			decimal cciPrev = cci[idx - 1];
			decimal cciPrev2 = cci[idx - 2];
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// Classic oversold recovery at -100
				if (cciPrev <= -100m && cciNow > -100m)
				{
					bool momentum = cciNow > cciPrev && cciPrev > cciPrev2;
					validation.IsValid = true;
					validation.Confidence = momentum ? 0.80m : 0.70m;
					validation.Reason = $"CCI oversold recovery ({cciPrev:F0} → {cciNow:F0})";
				}
				// Zero-line crossover (weaker signal)
				else if (cciPrev < 0 && cciNow > 0 && cciNow > cciPrev)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"CCI zero-line cross ({cciNow:F0})";
				}
				else
				{
					return validation.Fail($"No CCI buy signal (current={cciNow:F0})");
				}
			}
			else // Sell
			{
				// Classic overbought reversal at +100
				if (cciPrev >= 100m && cciNow < 100m)
				{
					bool momentum = cciNow < cciPrev && cciPrev < cciPrev2;
					validation.IsValid = true;
					validation.Confidence = momentum ? 0.80m : 0.70m;
					validation.Reason = $"CCI overbought reversal ({cciPrev:F0} → {cciNow:F0})";
				}
				// Zero-line crossover (weaker signal)
				else if (cciPrev > 0 && cciNow < 0 && cciNow < cciPrev)
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"CCI zero-line cross ({cciNow:F0})";
				}
				else
				{
					return validation.Fail($"No CCI sell signal (current={cciNow:F0})");
				}
			}

			return validation;
		}

		/// <summary>
		/// Volume spike validation with 1.5x threshold
		/// </summary>
		public static SignalValidation ValidateVolumeSpikeSwing(
			List<decimal> volumes, List<decimal> prices, int idx, decimal spikeThreshold = 1.5m)
		{
			var validation = new SignalValidation { IsValid = false };

			if (volumes == null || volumes.Count <= idx || idx < 30)
				return validation.Fail("Insufficient volume data");

			decimal currentVol = volumes[idx];
			var recentVols = volumes.Skip(idx - 30).Take(30).ToList();
			decimal avgVol = recentVols.Average();
			decimal medianVol = recentVols.OrderBy(v => v).ElementAt(15);

			if (avgVol <= 0)
				return validation.Fail("Invalid volume baseline");

			decimal volMultiple = currentVol / avgVol;
			decimal volVsMedian = currentVol / medianVol;

			// Need both average and median to confirm (avoids single-day spikes skewing average)
			if (volMultiple < spikeThreshold || volVsMedian < spikeThreshold * 0.8m)
				return validation.Fail($"No volume spike ({volMultiple:F2}x avg, {volVsMedian:F2}x median)");

			bool upBar = prices[idx] > prices[idx - 1];

			// Calculate volume trend (increasing volume = better)
			decimal vol3DayAvg = volumes.Skip(idx - 2).Take(3).Average();
			decimal vol10DayAvg = volumes.Skip(idx - 9).Take(10).Average();
			bool increasingVolume = vol3DayAvg > vol10DayAvg * 1.1m;

			validation.IsValid = true;
			validation.Confidence = Math.Min(0.60m + (volMultiple - spikeThreshold) * 0.15m, 0.90m);

			if (increasingVolume)
			{
				validation.Confidence += 0.08m;
				validation.Reason = $"Volume spike {volMultiple:F2}x on {(upBar ? "up" : "down")} bar + rising volume";
			}
			else
			{
				validation.Reason = $"Volume spike {volMultiple:F2}x on {(upBar ? "up" : "down")} bar";
			}

			return validation;
		}

		#endregion

		#region COMPOSITE SIGNAL SCORING

		/// <summary>
		/// Composite signal quality score
		/// Use this to filter only the best signals
		/// </summary>
		public class CompositeSignalScore
		{
			public decimal TotalScore { get; set; }
			public bool MeetsPrimaryFilters { get; set; }
			public int ConfirmationCount { get; set; }
			public string[] PassedValidations { get; set; } = Array.Empty<string>();
			public string[] FailedValidations { get; set; } = Array.Empty<string>();
			public string Recommendation { get; set; } = "";

		}
		public static CompositeSignalScore CalculateCompositeScore(
			SignalValidation trendStrength,
			SignalValidation choppiness,
			SignalValidation trendAlignment,
			SignalValidation ema,
			SignalValidation rsi,
			SignalValidation macd,
			SignalValidation supertrend,
			SignalValidation volume)
		{
			var score = new CompositeSignalScore();
			var passed = new List<string>();
			var failed = new List<string>();

			// PRIMARY FILTERS (all must pass)
			score.MeetsPrimaryFilters =
				trendStrength.IsValid &&
				choppiness.IsValid &&
				trendAlignment.IsValid;

			if (!score.MeetsPrimaryFilters)
			{
				if (!trendStrength.IsValid) failed.Add($"TrendStrength: {trendStrength.Reason}");
				if (!choppiness.IsValid) failed.Add($"Choppiness: {choppiness.Reason}");
				if (!trendAlignment.IsValid) failed.Add($"TrendAlign: {trendAlignment.Reason}");

				score.FailedValidations = failed.ToArray();
				score.Recommendation = "REJECT - Primary filters not met";
				return score;
			}

			passed.Add($"TrendStrength: {trendStrength.Confidence:P0}");
			passed.Add($"Choppiness: {choppiness.Confidence:P0}");
			passed.Add($"TrendAlign: {trendAlignment.Confidence:P0}");

			// ENTRY SIGNAL SCORING
			var entryValidations = new[]
			{
				(Name: "EMA", Val: ema, Weight: 0.20m),
				(Name: "RSI", Val: rsi, Weight: 0.20m),
				(Name: "MACD", Val: macd, Weight: 0.20m),
				(Name: "Supertrend", Val: supertrend, Weight: 0.20m)
			};

			decimal entryScore = 0m;
			int confirmations = 0;

			foreach (var entry in entryValidations)
			{
				if (entry.Val.IsValid)
				{
					entryScore += entry.Val.Confidence * entry.Weight;
					confirmations++;
					passed.Add($"{entry.Name}: {entry.Val.Confidence:P0}");
				}
				else
				{
					failed.Add($"{entry.Name}: {entry.Val.Reason}");
				}
			}

			// CONFIRMATION BONUS (volume)
			if (volume.IsValid)
			{
				entryScore += volume.Confidence * 0.20m;
				confirmations++;
				passed.Add($"Volume: {volume.Confidence:P0}");
			}
			else
			{
				failed.Add($"Volume: {volume.Reason}");
			}

			// PRIMARY FILTER CONTRIBUTION (30% of total)
			decimal primaryScore =
				(trendStrength.Confidence * 0.12m) +
				(choppiness.Confidence * 0.10m) +
				(trendAlignment.Confidence * 0.08m);

			score.TotalScore = primaryScore + entryScore;
			score.ConfirmationCount = confirmations;
			score.PassedValidations = passed.ToArray();
			score.FailedValidations = failed.ToArray();

			// RECOMMENDATION
			if (score.TotalScore >= 0.75m && confirmations >= 4)
			{
				score.Recommendation = "STRONG BUY/SELL - High quality signal";
			}
			else if (score.TotalScore >= 0.65m && confirmations >= 3)
			{
				score.Recommendation = "MODERATE - Acceptable signal quality";
			}
			else if (score.TotalScore >= 0.55m && confirmations >= 2)
			{
				score.Recommendation = "WEAK - Consider smaller position size";
			}
			else
			{
				score.Recommendation = "REJECT - Insufficient confirmations";
			}

			return score;
		}

		#endregion

		#region UTILITY INDICATORS TO ADD

		/// <summary>
		/// Volume Weighted Average Price - Institutional benchmark
		/// </summary>
		public static List<decimal> VWAPList(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, List<decimal> volumes)
		{
			var vwap = new List<decimal>();
			if (closes.Count == 0 || volumes.Count != closes.Count) return vwap;

			decimal cumulativeTPV = 0m;
			decimal cumulativeVolume = 0m;

			for (int i = 0; i < closes.Count; i++)
			{
				decimal typicalPrice = (highs[i] + lows[i] + closes[i]) / 3m;
				cumulativeTPV += typicalPrice * volumes[i];
				cumulativeVolume += volumes[i];

				decimal vwapValue = cumulativeVolume > 0 ? cumulativeTPV / cumulativeVolume : typicalPrice;
				vwap.Add(Math.Round(vwapValue, 4));
			}

			return vwap;
		}

		/// <summary>
		/// VWAP validation - Price position relative to VWAP
		/// </summary>
		public static SignalValidation ValidateVWAP(
			List<decimal> closes, List<decimal> vwap, int idx, string direction)
		{
			var validation = new SignalValidation { IsValid = false };

			if (idx < 20 || vwap.Count <= idx)
				return validation.Fail("Insufficient VWAP data");

			decimal price = closes[idx];
			decimal vwapValue = vwap[idx];
			decimal deviation = (price - vwapValue) / vwapValue;
			bool isBuy = direction == "Buy";

			if (isBuy)
			{
				// For buy: price should be near or above VWAP
				if (deviation < -0.03m) // More than 3% below VWAP
					return validation.Fail($"Price too far below VWAP ({deviation:P2})");

				if (deviation > 0.01m && deviation < 0.03m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.75m;
					validation.Reason = $"Price above VWAP ({deviation:P2}) - institutional support";
				}
				else if (deviation >= -0.01m && deviation <= 0.01m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"Price at VWAP ({deviation:P2}) - neutral zone";
				}
				else
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"Price deviation from VWAP ({deviation:P2})";
				}
			}
			else // Sell
			{
				if (deviation > 0.03m) // More than 3% above VWAP
					return validation.Fail($"Price too far above VWAP ({deviation:P2})");

				if (deviation < -0.01m && deviation > -0.03m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.75m;
					validation.Reason = $"Price below VWAP ({deviation:P2}) - institutional resistance";
				}
				else if (deviation >= -0.01m && deviation <= 0.01m)
				{
					validation.IsValid = true;
					validation.Confidence = 0.70m;
					validation.Reason = $"Price at VWAP ({deviation:P2}) - neutral zone";
				}
				else
				{
					validation.IsValid = true;
					validation.Confidence = 0.60m;
					validation.Reason = $"Price deviation from VWAP ({deviation:P2})";
				}
			}

			return validation;
		}

		#endregion
	}
}
