using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Enhanced Market Regime Detection System
	///
	/// Improvements over basic regime detection:
	/// 1. Multi-timeframe regime analysis (daily, weekly, monthly context)
	/// 2. Volatility clustering detection (GARCH-like approach)
	/// 3. Trend strength with acceleration metrics
	/// 4. Volume regime classification
	/// 5. Market breadth indicators (if multiple symbols available)
	/// 6. Risk-on/Risk-off sentiment proxy
	/// 7. Regime transition detection (early warning)
	/// 8. Adaptive thresholds based on recent market behavior
	///
	/// This helps the bot:
	/// - Adjust strategy weights based on current regime
	/// - Increase position sizes in favorable regimes
	/// - Reduce exposure or avoid trades in unfavorable regimes
	/// - Detect regime transitions early for better timing
	/// </summary>
	public static class EnhancedMarketRegime
	{
		public class RegimeAnalysis
		{
			// Primary regime
			public string PrimaryRegime { get; set; } = "Unknown";          // Trending/Ranging/Transitioning/Volatile
			public string TrendDirection { get; set; } = "Neutral";         // Bullish/Bearish/Neutral
			public decimal TrendStrength { get; set; }                      // 0-1 scale
			public decimal TrendAcceleration { get; set; }                  // Positive=accelerating, negative=decelerating

			// Volatility regime
			public string VolatilityRegime { get; set; } = "Normal";       // Low/Normal/High/Extreme
			public decimal VolatilityPercentile { get; set; }              // Current vol vs 60-day history (0-1)
			public bool VolatilityExpanding { get; set; }                  // True if volatility increasing
			public decimal VolatilityCluster { get; set; }                 // GARCH-like persistence metric

			// Volume regime
			public string VolumeRegime { get; set; } = "Normal";           // Low/Normal/High
			public decimal VolumePercentile { get; set; }                  // vs 30-day average
			public bool VolumeIncreasing { get; set; }                     // Volume trend direction

			// Multi-timeframe
			public string DailyRegime { get; set; } = "Unknown";
			public string WeeklyRegime { get; set; } = "Unknown";
			public bool TimeframesAligned { get; set; }                    // All TFs agree on direction

			// Regime quality
			public decimal RegimeConfidence { get; set; }                  // 0-1, how clear is the regime
			public decimal RegimeStability { get; set; }                   // 0-1, how long in current regime
			public bool InRegimeTransition { get; set; }                   // Early warning flag

			// Trading recommendations
			public decimal PositionSizeMultiplier { get; set; } = 1.0m;   // Adjust position size based on regime
			public decimal ConfidenceMultiplier { get; set; } = 1.0m;     // Adjust signal confidence
			public string[] FavoredStrategies { get; set; } = Array.Empty<string>(); // Which strategies work best
			public string RecommendedAction { get; set; } = "Normal";     // Aggressive/Normal/Defensive/Avoid

			// Risk metrics
			public decimal EstimatedMaxDrawdown { get; set; }             // Expected max DD in this regime
			public decimal RecommendedStopMultiplier { get; set; } = 1.0m; // Widen/tighten stops

			public override string ToString() =>
				$"{PrimaryRegime} ({TrendDirection}, Vol={VolatilityRegime}, {RecommendedAction})";
		}

		/// <summary>
		/// Comprehensive market regime analysis
		/// </summary>
		public static RegimeAnalysis AnalyzeRegime(
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			var regime = new RegimeAnalysis();

			if (closes.Count < 60)
			{
				regime.RegimeConfidence = 0m;
				return regime;
			}

			int idx = closes.Count - 1;

			// ═══════════════════════════════════════════════════════════════
			// 1. TREND REGIME ANALYSIS
			// ═══════════════════════════════════════════════════════════════

			var trendAnalysis = AnalyzeTrendRegime(closes, highs, lows);
			regime.PrimaryRegime = trendAnalysis.Regime;
			regime.TrendDirection = trendAnalysis.Direction;
			regime.TrendStrength = trendAnalysis.Strength;
			regime.TrendAcceleration = trendAnalysis.Acceleration;

			// ═══════════════════════════════════════════════════════════════
			// 2. VOLATILITY REGIME ANALYSIS (with clustering)
			// ═══════════════════════════════════════════════════════════════

			var volAnalysis = AnalyzeVolatilityRegime(highs, lows, closes);
			regime.VolatilityRegime = volAnalysis.Regime;
			regime.VolatilityPercentile = volAnalysis.Percentile;
			regime.VolatilityExpanding = volAnalysis.Expanding;
			regime.VolatilityCluster = volAnalysis.ClusterStrength;

			// ═══════════════════════════════════════════════════════════════
			// 3. VOLUME REGIME ANALYSIS
			// ═══════════════════════════════════════════════════════════════

			var volRegime = AnalyzeVolumeRegime(volumes);
			regime.VolumeRegime = volRegime.Regime;
			regime.VolumePercentile = volRegime.Percentile;
			regime.VolumeIncreasing = volRegime.Increasing;

			// ═══════════════════════════════════════════════════════════════
			// 4. MULTI-TIMEFRAME REGIME
			// ═══════════════════════════════════════════════════════════════

			var mtfRegime = AnalyzeMultiTimeframeRegime(closes, highs, lows);
			regime.DailyRegime = mtfRegime.DailyRegime;
			regime.WeeklyRegime = mtfRegime.WeeklyRegime;
			regime.TimeframesAligned = mtfRegime.Aligned;

			// ═══════════════════════════════════════════════════════════════
			// 5. REGIME TRANSITION DETECTION
			// ═══════════════════════════════════════════════════════════════

			regime.InRegimeTransition = DetectRegimeTransition(closes, highs, lows, volumes);
			regime.RegimeStability = CalculateRegimeStability(closes, highs, lows);

			// ═══════════════════════════════════════════════════════════════
			// 6. CALCULATE REGIME CONFIDENCE
			// ═══════════════════════════════════════════════════════════════

			regime.RegimeConfidence = CalculateRegimeConfidence(regime);

			// ═══════════════════════════════════════════════════════════════
			// 7. GENERATE TRADING RECOMMENDATIONS
			// ═══════════════════════════════════════════════════════════════

			GenerateTradingRecommendations(regime);

			return regime;
		}

		private static (string Regime, string Direction, decimal Strength, decimal Acceleration) AnalyzeTrendRegime(
			List<decimal> closes, List<decimal> highs, List<decimal> lows)
		{
			int idx = closes.Count - 1;

			// Calculate multiple EMAs for trend analysis
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);
			var ema200 = closes.Count >= 200 ? Indicators.EMAList(closes, 200) : ema50;

			// ADX for trend strength
			var (adx, diPlus, diMinus) = Indicators.ADXList(highs, lows, closes, 14);
			decimal adxVal = adx.Count > 0 ? adx[^1] : 0m;

			// Linear regression for trend
			var lrSlope = CalculateLinearRegressionSlope(closes, 20);

			// Determine trend direction
			decimal price = closes[idx];
			bool aboveEMA20 = price > ema20[idx];
			bool aboveEMA50 = price > ema50[idx];
			bool aboveEMA200 = price > ema200[idx];
			bool ema20Above50 = ema20[idx] > ema50[idx];
			bool ema50Above200 = ema50[idx] > ema200[idx];

			int bullishSignals = (aboveEMA20 ? 1 : 0) + (aboveEMA50 ? 1 : 0) + (aboveEMA200 ? 1 : 0) +
								(ema20Above50 ? 1 : 0) + (ema50Above200 ? 1 : 0) + (lrSlope > 0 ? 1 : 0);

			string direction = bullishSignals >= 4 ? "Bullish" : bullishSignals <= 2 ? "Bearish" : "Neutral";

			// Trend strength (0-1)
			decimal adxStrength = Math.Min(adxVal / 40m, 1m); // 40+ ADX = very strong
			decimal emaStrength = Math.Abs(ema20[idx] - ema50[idx]) / ema50[idx];
			decimal trendStrength = (adxStrength + Math.Min(emaStrength / 0.05m, 1m)) / 2m;

			// Trend acceleration (comparing recent vs older trend)
			decimal recentSlope = CalculateLinearRegressionSlope(closes.Skip(idx - 9).Take(10).ToList(), 10);
			decimal olderSlope = CalculateLinearRegressionSlope(closes.Skip(idx - 19).Take(10).ToList(), 10);
			decimal acceleration = recentSlope - olderSlope;

			// Determine regime
			string regime;
			if (adxVal > 25m && trendStrength > 0.5m)
				regime = "Trending";
			else if (adxVal < 20m || trendStrength < 0.3m)
				regime = "Ranging";
			else if (Math.Abs(acceleration) > 0.001m)
				regime = "Transitioning";
			else
				regime = "Choppy";

			return (regime, direction, trendStrength, acceleration);
		}

		private static (string Regime, decimal Percentile, bool Expanding, decimal ClusterStrength) AnalyzeVolatilityRegime(
			List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			int idx = closes.Count - 1;

			// Calculate ATR for current volatility
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count == 0) return ("Unknown", 0m, false, 0m);

			decimal currentATR = atr[^1];
			decimal currentPrice = closes[idx];
			decimal currentVolPct = currentATR / currentPrice;

			// Calculate volatility percentile vs 60-day history
			var atrHistory = atr.Skip(Math.Max(0, atr.Count - 60)).ToList();
			int rank = atrHistory.Count(a => a <= currentATR);
			decimal percentile = atrHistory.Count > 0 ? (decimal)rank / atrHistory.Count : 0.5m;

			// Volatility expansion/contraction
			decimal atr5Ago = atr.Count > 5 ? atr[^6] : currentATR;
			decimal atr10Ago = atr.Count > 10 ? atr[^11] : currentATR;
			bool expanding = currentATR > atr5Ago && atr5Ago > atr10Ago;

			// Volatility clustering (GARCH-like)
			// High recent vol predicts high future vol
			decimal avgRecent5 = atr.Skip(Math.Max(0, atr.Count - 5)).Take(5).Average();
			decimal avgOlder20 = atr.Skip(Math.Max(0, atr.Count - 25)).Take(20).Average();
			decimal clusterStrength = avgOlder20 > 0 ? avgRecent5 / avgOlder20 : 1m;

			// Classify regime
			string regime;
			if (percentile > 0.80m || currentVolPct > 0.08m)
				regime = "Extreme";
			else if (percentile > 0.65m || currentVolPct > 0.05m)
				regime = "High";
			else if (percentile < 0.35m || currentVolPct < 0.015m)
				regime = "Low";
			else
				regime = "Normal";

			return (regime, percentile, expanding, clusterStrength);
		}

		private static (string Regime, decimal Percentile, bool Increasing) AnalyzeVolumeRegime(List<decimal> volumes)
		{
			if (volumes.Count < 30)
				return ("Unknown", 0.5m, false);

			int idx = volumes.Count - 1;
			decimal currentVol = volumes[idx];

			// 30-day average
			var recent30 = volumes.Skip(Math.Max(0, idx - 29)).Take(30).ToList();
			decimal avg30 = recent30.Average();

			// Volume percentile
			int rank = recent30.Count(v => v <= currentVol);
			decimal percentile = (decimal)rank / recent30.Count;

			// Volume trend
			decimal avg5Recent = volumes.Skip(Math.Max(0, idx - 4)).Take(5).Average();
			decimal avg10Older = volumes.Skip(Math.Max(0, idx - 14)).Take(10).Average();
			bool increasing = avg5Recent > avg10Older;

			// Classify
			string regime;
			if (percentile > 0.75m)
				regime = "High";
			else if (percentile < 0.40m)
				regime = "Low";
			else
				regime = "Normal";

			return (regime, percentile, increasing);
		}

		private static (string DailyRegime, string WeeklyRegime, bool Aligned) AnalyzeMultiTimeframeRegime(
			List<decimal> closes, List<decimal> highs, List<decimal> lows)
		{
			int idx = closes.Count - 1;

			// Daily regime (20-50 day EMAs)
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			string dailyRegime;
			if (closes[idx] > ema20[idx] && ema20[idx] > ema50[idx])
				dailyRegime = "Bullish";
			else if (closes[idx] < ema20[idx] && ema20[idx] < ema50[idx])
				dailyRegime = "Bearish";
			else
				dailyRegime = "Neutral";

			// Weekly regime (simulated with 50-100 day EMAs)
			var ema100 = closes.Count >= 100 ? Indicators.EMAList(closes, 100) : ema50;

			string weeklyRegime;
			if (ema50[idx] > ema100[idx] && closes[idx] > ema50[idx])
				weeklyRegime = "Bullish";
			else if (ema50[idx] < ema100[idx] && closes[idx] < ema50[idx])
				weeklyRegime = "Bearish";
			else
				weeklyRegime = "Neutral";

			bool aligned = (dailyRegime == weeklyRegime) && dailyRegime != "Neutral";

			return (dailyRegime, weeklyRegime, aligned);
		}

		private static bool DetectRegimeTransition(
			List<decimal> closes, List<decimal> highs, List<decimal> lows, List<decimal> volumes)
		{
			if (closes.Count < 30) return false;

			int idx = closes.Count - 1;

			// Multiple transition signals
			int transitionSignals = 0;

			// 1. EMA crossovers happening
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			// Check if EMAs are very close (within 1%)
			decimal emaSeparation = Math.Abs(ema20[idx] - ema50[idx]) / ema50[idx];
			if (emaSeparation < 0.01m)
				transitionSignals++;

			// 2. Volatility spike (often happens at transitions)
			var atr = Indicators.ATRList(highs, lows, closes, 14);
			if (atr.Count > 5)
			{
				decimal currentATR = atr[^1];
				decimal avgATR = atr.Skip(Math.Max(0, atr.Count - 20)).Take(20).Average();
				if (currentATR > avgATR * 1.3m)
					transitionSignals++;
			}

			// 3. Volume spike (breakout potential)
			if (volumes.Count > 20)
			{
				decimal currentVol = volumes[idx];
				decimal avgVol = volumes.Skip(Math.Max(0, idx - 19)).Take(20).Average();
				if (currentVol > avgVol * 1.5m)
					transitionSignals++;
			}

			// 4. Decreasing ADX (trend weakening)
			var (adx, _, _) = Indicators.ADXList(highs, lows, closes, 14);
			if (adx.Count > 5)
			{
				decimal currentADX = adx[^1];
				decimal adx5Ago = adx[^6];
				if (currentADX < adx5Ago && currentADX < 25m)
					transitionSignals++;
			}

			return transitionSignals >= 2;
		}

		private static decimal CalculateRegimeStability(List<decimal> closes, List<decimal> highs, List<decimal> lows)
		{
			// How long has the current trend been in place?
			// Higher = more stable regime

			if (closes.Count < 20) return 0m;

			int idx = closes.Count - 1;
			var ema20 = Indicators.EMAList(closes, 20);
			var ema50 = Indicators.EMAList(closes, 50);

			// Count consecutive days with same EMA alignment
			int daysInCurrentRegime = 0;
			bool currentRegimeBullish = ema20[idx] > ema50[idx];

			for (int i = idx; i >= Math.Max(0, idx - 40); i--)
			{
				bool wasBullish = ema20[i] > ema50[i];
				if (wasBullish == currentRegimeBullish)
					daysInCurrentRegime++;
				else
					break;
			}

			// Normalize to 0-1 (40+ days = very stable)
			return Math.Min(daysInCurrentRegime / 40m, 1m);
		}

		private static decimal CalculateRegimeConfidence(RegimeAnalysis regime)
		{
			decimal confidence = 0.5m;

			// Higher confidence if:
			// 1. Clear trend direction
			if (regime.TrendStrength > 0.6m)
				confidence += 0.15m;

			// 2. Timeframes aligned
			if (regime.TimeframesAligned)
				confidence += 0.15m;

			// 3. Not in transition
			if (!regime.InRegimeTransition)
				confidence += 0.10m;

			// 4. Regime is stable
			confidence += regime.RegimeStability * 0.10m;

			// Lower confidence if:
			// 1. Extreme volatility
			if (regime.VolatilityRegime == "Extreme")
				confidence -= 0.20m;

			// 2. Low volume
			if (regime.VolumeRegime == "Low")
				confidence -= 0.10m;

			return Math.Max(0m, Math.Min(1m, confidence));
		}

		private static void GenerateTradingRecommendations(RegimeAnalysis regime)
		{
			// ═══════════════════════════════════════════════════════════════
			// POSITION SIZE MULTIPLIER
			// ═══════════════════════════════════════════════════════════════

			decimal sizeMultiplier = 1.0m;

			// Favorable conditions - increase size
			if (regime.TimeframesAligned && regime.TrendStrength > 0.6m && !regime.InRegimeTransition)
				sizeMultiplier += 0.3m; // +30%

			if (regime.VolatilityRegime == "Normal" || regime.VolatilityRegime == "Low")
				sizeMultiplier += 0.2m; // +20%

			if (regime.VolumeIncreasing && regime.VolumeRegime != "Low")
				sizeMultiplier += 0.1m; // +10%

			// Unfavorable conditions - reduce size
			if (regime.VolatilityRegime == "Extreme" || regime.VolatilityRegime == "High")
				sizeMultiplier -= 0.4m; // -40%

			if (regime.InRegimeTransition)
				sizeMultiplier -= 0.2m; // -20%

			if (regime.PrimaryRegime == "Choppy" || regime.PrimaryRegime == "Ranging")
				sizeMultiplier -= 0.3m; // -30%

			if (regime.VolumeRegime == "Low")
				sizeMultiplier -= 0.2m; // -20%

			regime.PositionSizeMultiplier = Math.Max(0.2m, Math.Min(1.5m, sizeMultiplier));

			// ═══════════════════════════════════════════════════════════════
			// CONFIDENCE MULTIPLIER
			// ═══════════════════════════════════════════════════════════════

			decimal confMultiplier = 1.0m;

			if (regime.TimeframesAligned)
				confMultiplier += 0.15m;

			if (regime.TrendStrength > 0.7m)
				confMultiplier += 0.1m;

			if (regime.VolatilityExpanding && regime.TrendDirection != "Neutral")
				confMultiplier += 0.05m; // Volatility expansion in trend direction

			if (regime.InRegimeTransition)
				confMultiplier -= 0.2m;

			if (regime.VolatilityRegime == "Extreme")
				confMultiplier -= 0.15m;

			regime.ConfidenceMultiplier = Math.Max(0.5m, Math.Min(1.3m, confMultiplier));

			// ═══════════════════════════════════════════════════════════════
			// FAVORED STRATEGIES
			// ═══════════════════════════════════════════════════════════════

			var favoredStrategies = new List<string>();

			if (regime.PrimaryRegime == "Trending")
			{
				favoredStrategies.AddRange(new[] { "TrendFollowing", "Momentum", "Breakout" });
			}
			else if (regime.PrimaryRegime == "Ranging")
			{
				favoredStrategies.AddRange(new[] { "MeanReversion", "SupportResistance", "Bollinger" });
			}
			else if (regime.PrimaryRegime == "Transitioning")
			{
				favoredStrategies.AddRange(new[] { "Breakout", "VolumeConfirm" });
			}

			regime.FavoredStrategies = favoredStrategies.ToArray();

			// ═══════════════════════════════════════════════════════════════
			// RECOMMENDED ACTION
			// ═══════════════════════════════════════════════════════════════

			if (regime.RegimeConfidence > 0.7m && regime.TrendStrength > 0.6m && !regime.InRegimeTransition)
				regime.RecommendedAction = "Aggressive";
			else if (regime.VolatilityRegime == "Extreme" || regime.InRegimeTransition || regime.RegimeConfidence < 0.4m)
				regime.RecommendedAction = "Defensive";
			else if (regime.PrimaryRegime == "Choppy" && regime.VolatilityRegime == "High")
				regime.RecommendedAction = "Avoid";
			else
				regime.RecommendedAction = "Normal";

			// ═══════════════════════════════════════════════════════════════
			// STOP LOSS MULTIPLIER
			// ═══════════════════════════════════════════════════════════════

			decimal stopMultiplier = 1.0m;

			// Widen stops in high volatility
			if (regime.VolatilityRegime == "High")
				stopMultiplier += 0.3m;
			else if (regime.VolatilityRegime == "Extreme")
				stopMultiplier += 0.5m;

			// Tighten stops in low volatility
			if (regime.VolatilityRegime == "Low")
				stopMultiplier -= 0.2m;

			// Widen stops in transitions (avoid whipsaws)
			if (regime.InRegimeTransition)
				stopMultiplier += 0.2m;

			regime.RecommendedStopMultiplier = Math.Max(0.7m, Math.Min(1.5m, stopMultiplier));

			// ═══════════════════════════════════════════════════════════════
			// ESTIMATED MAX DRAWDOWN
			// ═══════════════════════════════════════════════════════════════

			decimal estimatedDD = 0.05m; // Base 5%

			if (regime.VolatilityRegime == "High")
				estimatedDD += 0.03m;
			else if (regime.VolatilityRegime == "Extreme")
				estimatedDD += 0.06m;

			if (regime.PrimaryRegime == "Choppy")
				estimatedDD += 0.02m;

			regime.EstimatedMaxDrawdown = estimatedDD;
		}

		private static decimal CalculateLinearRegressionSlope(List<decimal> values, int period)
		{
			if (values.Count < period) return 0m;

			var recentValues = values.Skip(values.Count - period).ToList();
			int n = recentValues.Count;

			decimal sumX = 0m;
			decimal sumY = 0m;
			decimal sumXY = 0m;
			decimal sumX2 = 0m;

			for (int i = 0; i < n; i++)
			{
				decimal x = i;
				decimal y = recentValues[i];
				sumX += x;
				sumY += y;
				sumXY += x * y;
				sumX2 += x * x;
			}

			decimal slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

			// Normalize slope by average price
			decimal avgPrice = sumY / n;
			return avgPrice > 0 ? slope / avgPrice : 0m;
		}
	}
}
