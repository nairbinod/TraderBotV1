using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Enhanced indicators with advanced filtering and market regime detection
	/// </summary>
	public static class IndicatorsEnhanced
	{
		// ═══════════════════════════════════════════════════════════════
		// MARKET REGIME DETECTION (Critical for reducing false signals)
		// ═══════════════════════════════════════════════════════════════

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

		/// <summary>
		/// Advanced market regime detection using multiple indicators
		/// </summary>
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
			var (adx, diPlus, diMinus) = IndicatorsExtended.ADXList(highs, lows, closes, 14);
			decimal adxValue = adx.Count > idx ? adx[idx] : 0m;

			// Calculate volatility metrics
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			decimal currentATR = atr.Count > idx ? atr[idx] : 0m;
			decimal price = closes[idx];
			decimal atrPercent = price > 0 ? (currentATR / price) : 0m;

			// Calculate price movement
			var recentPrices = closes.Skip(Math.Max(0, idx - lookback)).Take(lookback).ToList();
			decimal priceRange = recentPrices.Max() - recentPrices.Min();
			decimal avgPrice = recentPrices.Average();
			decimal rangePercent = avgPrice > 0 ? (priceRange / avgPrice) : 0m;

			// EMA alignment check
			var ema20 = Indicators.EMA(closes.Take(idx + 1).ToList(), 20);
			var ema50 = Indicators.EMA(closes.Take(idx + 1).ToList(), 50);
			var ema200 = idx >= 200 ? Indicators.EMA(closes.Take(idx + 1).ToList(), 200) : ema50;

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

		// ═══════════════════════════════════════════════════════════════
		// MULTI-TIMEFRAME ANALYSIS
		// ═══════════════════════════════════════════════════════════════

		public class MultiTimeframeSignal
		{
			public string HigherTFTrend { get; set; } = "Neutral";
			public string CurrentTFTrend { get; set; } = "Neutral";
			public bool IsAligned { get; set; }
			public decimal Confidence { get; set; }
			public string Reason { get; set; } = "";
		}

		/// <summary>
		/// Analyzes trend across multiple timeframes by downsampling
		/// </summary>
		public static MultiTimeframeSignal AnalyzeMultiTimeframe(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows)
		{
			if (closes.Count < 100)
				return new MultiTimeframeSignal { Reason = "Insufficient data" };

			// Current timeframe trend
			var currentEma20 = Indicators.EMA(closes, 20);
			var currentEma50 = Indicators.EMA(closes, 50);
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
			var htfEma20 = Indicators.EMA(htfCloses, 20);
			var htfEma50 = Indicators.EMA(htfCloses, 50);
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

		// ═══════════════════════════════════════════════════════════════
		// DYNAMIC SUPPORT/RESISTANCE
		// ═══════════════════════════════════════════════════════════════

		public class SupportResistanceLevel
		{
			public decimal Level { get; set; }
			public int Touches { get; set; }
			public decimal Strength { get; set; }
			public bool IsSupport { get; set; }
		}

		/// <summary>
		/// Identifies dynamic support and resistance levels
		/// </summary>
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

					// Count touches
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

			// Cluster similar levels
			return ClusterLevels(levels, tolerance);
		}

		private static int CountTouches(List<decimal> prices, decimal level, decimal tolerance)
		{
			decimal range = level * tolerance;
			return prices.Count(p => Math.Abs(p - level) <= range);
		}

		private static List<SupportResistanceLevel> ClusterLevels(
			List<SupportResistanceLevel> levels,
			decimal tolerance)
		{
			if (levels.Count == 0) return levels;

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

		// ═══════════════════════════════════════════════════════════════
		// ADVANCED VOLUME ANALYSIS
		// ═══════════════════════════════════════════════════════════════

		public class VolumeAnalysis
		{
			public bool IsAccumulation { get; set; }
			public bool IsDistribution { get; set; }
			public decimal VolumeStrength { get; set; }
			public decimal VolumeRatio { get; set; }
			public string Signal { get; set; } = "Neutral";
		}

		/// <summary>
		/// Advanced volume analysis using OBV and volume trends
		/// </summary>
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
			var obvEma = Indicators.EMAList(obv, 10);

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

		// ═══════════════════════════════════════════════════════════════
		// CANDLESTICK PATTERN RECOGNITION
		// ═══════════════════════════════════════════════════════════════

		public class CandlestickPattern
		{
			public string PatternName { get; set; } = "";
			public string Signal { get; set; } = "Neutral";
			public decimal Strength { get; set; }
		}

		/// <summary>
		/// Recognizes key candlestick patterns
		/// </summary>
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

		// ═══════════════════════════════════════════════════════════════
		// HELPER METHODS
		// ═══════════════════════════════════════════════════════════════

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
	}
}