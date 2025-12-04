// ═══════════════════════════════════════════════════════════════════════════
// SIGNAL DIAGNOSTIC & FIX
// Problem: Bot generating ZERO buy signals
// Solution: Add diagnostics + balanced thresholds
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// STEP 1: Add this diagnostic class to identify WHERE signals are being blocked
	/// </summary>
	public static class SignalBlockageDiagnostic
	{
		public class DiagnosticResult
		{
			public string Symbol { get; set; } = "";
			public bool PassedVolatility { get; set; }
			public bool PassedChoppiness { get; set; }
			public bool PassedRegimeConfidence { get; set; }
			public bool PassedTrendStrength { get; set; }
			public bool PassedTrendDirection { get; set; }
			public bool PassedPrimaryFilters { get; set; }
			public bool PassedVoteCount { get; set; }
			public bool PassedVoteDominance { get; set; }
			public bool PassedQualityScore { get; set; }
			public bool PassedCompositeScore { get; set; }
			public bool PassedCoreStrategy { get; set; }

			public int BuyVotes { get; set; }
			public int SellVotes { get; set; }
			public decimal BuyConfidence { get; set; }
			public decimal SellConfidence { get; set; }
			public decimal QualityScore { get; set; }
			public decimal CompositeScore { get; set; }
			public decimal Volatility { get; set; }
			public decimal Choppiness { get; set; }
			public decimal TrendStrength { get; set; }
			public string PrimaryFilterDirection { get; set; } = "";
			public string BlockedAt { get; set; } = "";
			public string Details { get; set; } = "";
		}

		/// <summary>
		/// Run this BEFORE your normal EvaluateAndLog to see where signals are blocked
		/// </summary>
		public static DiagnosticResult DiagnoseSignalBlockage(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal>? volumes,
			List<decimal>? opens)
		{
			var result = new DiagnosticResult { Symbol = symbol };

			if (closes.Count < 120)
			{
				result.BlockedAt = "INSUFFICIENT_DATA";
				result.Details = $"Only {closes.Count} bars, need 120+";
				return result;
			}

			// Estimate volume if missing
			if (volumes == null || volumes.Count == 0)
				volumes = Indicators.EstimateVolume(closes, highs, lows);

			if (opens == null || opens.Count != closes.Count)
				opens = closes.Select((c, i) => i > 0 ? closes[i - 1] : c).ToList();

			int idx = closes.Count - 1;

			// ═══ CHECK 1: MARKET REGIME ═══
			var regime = Indicators.DetectMarketRegime(closes, highs, lows);
			result.Volatility = regime.VolatilityLevel;

			// Volatility check
			if (regime.VolatilityLevel > 0.20m)
			{
				result.BlockedAt = "VOLATILITY_HIGH";
				result.Details = $"Volatility {regime.VolatilityLevel:P2} > 20%";
				return result;
			}
			if (regime.VolatilityLevel < 0.004m)
			{
				result.BlockedAt = "VOLATILITY_LOW";
				result.Details = $"Volatility {regime.VolatilityLevel:P2} < 0.4%";
				return result;
			}
			result.PassedVolatility = true;

			// Choppiness check
			var choppiness = CalculateChoppinessSimple(highs, lows, closes, 14);
			result.Choppiness = choppiness;

			if (choppiness > 0.75m)
			{
				result.BlockedAt = "CHOPPINESS_HIGH";
				result.Details = $"Choppiness {choppiness:P0} > 75%";
				return result;
			}
			result.PassedChoppiness = true;

			// Regime confidence check
			if (regime.RegimeConfidence < 0.40m)
			{
				result.BlockedAt = "REGIME_CONFIDENCE_LOW";
				result.Details = $"Regime confidence {regime.RegimeConfidence:P0} < 40%";
				return result;
			}
			result.PassedRegimeConfidence = true;

			// ═══ CHECK 2: TREND STRENGTH ═══
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);
			result.TrendStrength = context.TrendStrength;

			if (context.TrendStrength < 0.005m)
			{
				result.BlockedAt = "TREND_STRENGTH_LOW";
				result.Details = $"Trend strength {context.TrendStrength:P2} < 0.5%";
				return result;
			}
			result.PassedTrendStrength = true;

			if (!context.IsUptrend && !context.IsDowntrend)
			{
				result.BlockedAt = "NO_CLEAR_TREND";
				result.Details = "Neither uptrend nor downtrend detected";
				return result;
			}
			result.PassedTrendDirection = true;

			// ═══ CHECK 3: PRIMARY FILTERS ═══
			var primaryFilter = Strategies.PrimaryFilters(closes, highs, lows);
			result.PrimaryFilterDirection = primaryFilter.Signal;

			if (primaryFilter.Signal == "Hold")
			{
				result.BlockedAt = "PRIMARY_FILTERS";
				result.Details = primaryFilter.Reason;
				return result;
			}
			result.PassedPrimaryFilters = true;

			string allowedDirection = primaryFilter.Signal;

			// ═══ CHECK 4: STRATEGY VOTING ═══
			var allSignals = ExecuteAllStrategies(closes, highs, lows, volumes, opens);

			var buySignals = allSignals
				.Where(s => s.signal.Signal == "Buy" &&
						   s.signal.Strength >= 0.48m &&
						   allowedDirection == "Buy")
				.ToList();

			var sellSignals = allSignals
				.Where(s => s.signal.Signal == "Sell" &&
						   s.signal.Strength >= 0.48m &&
						   allowedDirection == "Sell")
				.ToList();

			result.BuyVotes = buySignals.Count;
			result.SellVotes = sellSignals.Count;
			result.BuyConfidence = buySignals.Count > 0
				? buySignals.Average(s => s.signal.Strength) : 0m;
			result.SellConfidence = sellSignals.Count > 0
				? sellSignals.Average(s => s.signal.Strength) : 0m;

			// Vote count check
			int relevantVotes = allowedDirection == "Buy" ? buySignals.Count : sellSignals.Count;
			if (relevantVotes < 5)
			{
				result.BlockedAt = "INSUFFICIENT_VOTES";
				result.Details = $"Only {relevantVotes} {allowedDirection} votes (need 5). " +
					$"Total: {buySignals.Count} Buy, {sellSignals.Count} Sell";

				// Show which strategies voted
				var votingStrategies = (allowedDirection == "Buy" ? buySignals : sellSignals)
					.Select(s => $"s{s.index}:{s.signal.Strength:P0}")
					.ToList();
				result.Details += $"\nVoting strategies: {string.Join(", ", votingStrategies)}";

				return result;
			}
			result.PassedVoteCount = true;

			// Vote dominance check
			int totalVotes = buySignals.Count + sellSignals.Count;
			decimal voteDominance = totalVotes > 0
				? (decimal)relevantVotes / totalVotes : 0m;

			if (voteDominance < 0.50m)
			{
				result.BlockedAt = "VOTE_DOMINANCE_LOW";
				result.Details = $"Vote dominance {voteDominance:P0} < 50%";
				return result;
			}
			result.PassedVoteDominance = true;

			// Core strategy check
			var relevantSignals = allowedDirection == "Buy" ? buySignals : sellSignals;
			bool hasCoreConfirmation = relevantSignals.Any(s => s.index <= 8);

			if (!hasCoreConfirmation)
			{
				result.BlockedAt = "NO_CORE_STRATEGY";
				result.Details = "No core strategy (s0-s8) voted in direction";
				return result;
			}
			result.PassedCoreStrategy = true;

			// ═══ CHECK 5: QUALITY SCORE ═══
			decimal qualityScore = ImprovedQualityScore.CalculateSwingQualityScore(
				opens, closes, highs, lows, volumes, allowedDirection);
			result.QualityScore = qualityScore;

			if (qualityScore < 0.48m)
			{
				result.BlockedAt = "QUALITY_SCORE_LOW";
				result.Details = $"Quality {qualityScore:P0} < 48%";
				return result;
			}
			result.PassedQualityScore = true;

			// ═══ CHECK 6: COMPOSITE SCORE ═══
			// Simplified - would need full implementation
			result.PassedCompositeScore = true;
			result.CompositeScore = qualityScore * 0.9m + 0.1m;

			// ═══ ALL CHECKS PASSED ═══
			result.BlockedAt = "NONE - SIGNAL SHOULD GENERATE";
			result.Details = $"✅ All checks passed for {allowedDirection}. " +
				$"Votes: {relevantVotes}, Quality: {qualityScore:P0}";

			return result;
		}

		private static decimal CalculateChoppinessSimple(
			List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
		{
			if (closes.Count < period + 1) return 0.5m;

			int idx = closes.Count - 1;
			decimal sumTR = 0m;
			decimal highestHigh = 0m;
			decimal lowestLow = decimal.MaxValue;

			for (int i = idx - period + 1; i <= idx; i++)
			{
				decimal tr = Math.Max(highs[i] - lows[i],
					Math.Max(Math.Abs(highs[i] - closes[i - 1]),
							 Math.Abs(lows[i] - closes[i - 1])));
				sumTR += tr;
				highestHigh = Math.Max(highestHigh, highs[i]);
				lowestLow = Math.Min(lowestLow, lows[i]);
			}

			decimal range = highestHigh - lowestLow;
			if (range <= 0) return 0.5m;

			decimal ci = 100m * (decimal)Math.Log10((double)(sumTR / range)) /
						(decimal)Math.Log10(period);
			return ci / 100m;
		}

		private static List<(StrategySignal signal, int index)> ExecuteAllStrategies(
			List<decimal> closes, List<decimal> highs, List<decimal> lows,
			List<decimal> volumes, List<decimal> opens)
		{
			var emaShort = Indicators.EMAList(closes, 20);
			var emaLong = Indicators.EMAList(closes, 50);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 25, 2);
			var atr = Indicators.ATRList(highs, lows, closes, 20);

			var s1 = Strategies.TrendFollowingMTF(closes, highs, lows);
			var s2 = Strategies.MeanReversionSR(closes, highs, lows, volumes);
			var s3 = Strategies.BreakoutWithVolume(opens, closes, highs, lows, volumes);
			var s4 = Strategies.MomentumReversalDivergence(closes, highs, lows, volumes);
			var s5 = Strategies.EmaRsi(closes, 20, 50, 14);
			var s6 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s7 = Strategies.AtrBreakout(closes, highs, lows, atr, 20, 50, 14);
			var s8 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);
			var s9 = Strategies.AdxFilter(highs, lows, closes, 14, 18m);
			var s10 = Strategies.VolumeConfirm(closes, volumes, 30, 1.0m);
			var s11 = Strategies.DonchianBreakout(highs, lows, closes, 25);
			var s12 = Strategies.VWAPStrategy(closes, highs, lows, volumes);
			var s13 = Strategies.IchimokuCloud(closes, highs, lows);
			var s14 = Strategies.PriceActionTrend(closes, highs, lows);
			var s15 = Strategies.SupertrendStrategyEnhanced(highs, lows, closes, 14, 2.5m);
			var s16 = Strategies.MeanReversionMFI(closes, highs, lows, volumes, 14, 20);
			var s17 = Strategies.TripleMomentumStrategy(closes, highs, lows, volumes, 12);
			var s18 = Strategies.SupportResistanceBounce(closes, highs, lows, volumes);
			var s19 = Strategies.GapTradingStrategy(opens, closes, highs, lows, volumes, 0.005m);
			var s20 = Strategies.CMFMomentumStrategy(closes, highs, lows, volumes, 20);

			return new List<(StrategySignal signal, int index)>
			{
				(s1, 0), (s2, 1), (s3, 2), (s4, 3), (s5, 4),
				(s6, 5), (s7, 6), (s8, 7), (s9, 8), (s10, 9),
				(s11, 10), (s12, 11), (s13, 12), (s14, 13), (s15, 14),
				(s16, 15), (s17, 16), (s18, 17), (s19, 18), (s20, 19)
			};
		}

		/// <summary>
		/// Run diagnostic on multiple symbols and show summary
		/// </summary>
		public static void RunBatchDiagnostic(List<DiagnosticResult> results)
		{
			Console.WriteLine("\n" + new string('═', 70));
			Console.WriteLine("SIGNAL BLOCKAGE DIAGNOSTIC SUMMARY");
			Console.WriteLine(new string('═', 70));

			var blockageGroups = results
				.GroupBy(r => r.BlockedAt)
				.OrderByDescending(g => g.Count())
				.ToList();

			Console.WriteLine($"\nTotal symbols analyzed: {results.Count}");
			Console.WriteLine($"\nBlockage breakdown:");

			foreach (var group in blockageGroups)
			{
				decimal pct = (decimal)group.Count() / results.Count * 100m;
				Console.WriteLine($"  {group.Key,-30} : {group.Count(),4} ({pct:F1}%)");
			}

			// Show specific issues
			var insufficientVotes = results.Where(r => r.BlockedAt == "INSUFFICIENT_VOTES").ToList();
			if (insufficientVotes.Count > 0)
			{
				Console.WriteLine($"\n📊 INSUFFICIENT_VOTES Details:");
				Console.WriteLine($"   Average Buy Votes: {insufficientVotes.Average(r => r.BuyVotes):F1}");
				Console.WriteLine($"   Average Sell Votes: {insufficientVotes.Average(r => r.SellVotes):F1}");
				Console.WriteLine($"   Symbols close to threshold (4 votes):");

				var almostThere = insufficientVotes
					.Where(r => r.BuyVotes == 4 || r.SellVotes == 4)
					.Take(5);
				foreach (var r in almostThere)
				{
					Console.WriteLine($"     {r.Symbol}: {r.BuyVotes}B/{r.SellVotes}S");
				}
			}

			var primaryFilterFails = results.Where(r => r.BlockedAt == "PRIMARY_FILTERS").ToList();
			if (primaryFilterFails.Count > 0)
			{
				Console.WriteLine($"\n📊 PRIMARY_FILTERS Failures:");
				var reasons = primaryFilterFails
					.GroupBy(r => r.Details)
					.OrderByDescending(g => g.Count())
					.Take(5);
				foreach (var reason in reasons)
				{
					Console.WriteLine($"   {reason.Key}: {reason.Count()}");
				}
			}

			Console.WriteLine(new string('═', 70));
		}
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// STEP 2: BALANCED THRESHOLDS - Replace your current constants
	// ═══════════════════════════════════════════════════════════════════════════

	/*
    COPY THESE INTO TradeEngineConservative.cs, replacing lines 54-68:

    // ═══ BALANCED SWING THRESHOLDS (Generate Signals Edition) ═══
    // Problem: ULTRA-RELAXED still blocks everything
    // Solution: Focus on WHICH filters are blocking
    
    private const int MIN_VOTES_REQUIRED = 4;              // ⚠️ DOWN from 5 - this is likely the blocker
    private const decimal MIN_STRATEGY_CONFIDENCE = 0.45m; // DOWN from 0.48
    private const decimal MIN_FINAL_CONFIDENCE = 0.48m;    // DOWN from 0.50
    private const decimal MIN_QUALITY_SCORE = 0.45m;       // DOWN from 0.48
    private const int MIN_STRATEGIES_FOR_ENTRY = 4;        // DOWN from 5

    private const decimal MIN_VOTE_DOMINANCE = 0.45m;      // DOWN from 0.50 - mixed signals okay
    private const decimal MIN_TREND_STRENGTH = 0.003m;     // DOWN from 0.005
    private const decimal MIN_SIGNAL_GAP = 0.02m;          // DOWN from 0.03
    private const decimal MAX_CHOPPINESS = 0.80m;          // UP from 0.75 - allow choppier markets

    // Keep these the same - unlikely blockers
    private const decimal MIN_COMPOSITE_SCORE = 0.60m;     // DOWN from 0.65
    private const int MIN_CONFIRMATIONS = 2;               // DOWN from 3
    */

	// ═══════════════════════════════════════════════════════════════════════════
	// STEP 3: QUICK FIX - Bypass problematic filters temporarily
	// Add this alternate evaluation method
	// ═══════════════════════════════════════════════════════════════════════════

	public partial class TradeEngineConservative
	{
		/// <summary>
		/// DIAGNOSTIC VERSION: Shows where signal would be blocked
		/// Run this to see WHY you're getting no signals
		/// </summary>
		public void EvaluateWithDiagnostics(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal>? volumes,
			List<decimal>? opens,
			DateTime lastBarDate)
		{
			Console.WriteLine($"\n{'═'.ToString().PadRight(60, '═')}");
			Console.WriteLine($"DIAGNOSTIC MODE: {symbol}");
			Console.WriteLine($"{'═'.ToString().PadRight(60, '═')}");

			var diagnostic = SignalBlockageDiagnostic.DiagnoseSignalBlockage(
				symbol, closes, highs, lows, volumes, opens);

			// Show all filter results
			Console.WriteLine($"\n📋 Filter Results:");
			Console.WriteLine($"   Volatility:      {(diagnostic.PassedVolatility ? "✅" : "❌")} ({diagnostic.Volatility:P2})");
			Console.WriteLine($"   Choppiness:      {(diagnostic.PassedChoppiness ? "✅" : "❌")} ({diagnostic.Choppiness:P0})");
			Console.WriteLine($"   Regime Conf:     {(diagnostic.PassedRegimeConfidence ? "✅" : "❌")}");
			Console.WriteLine($"   Trend Strength:  {(diagnostic.PassedTrendStrength ? "✅" : "❌")} ({diagnostic.TrendStrength:P2})");
			Console.WriteLine($"   Trend Direction: {(diagnostic.PassedTrendDirection ? "✅" : "❌")}");
			Console.WriteLine($"   Primary Filters: {(diagnostic.PassedPrimaryFilters ? "✅" : "❌")} ({diagnostic.PrimaryFilterDirection})");
			Console.WriteLine($"   Vote Count:      {(diagnostic.PassedVoteCount ? "✅" : "❌")} (B:{diagnostic.BuyVotes} S:{diagnostic.SellVotes})");
			Console.WriteLine($"   Vote Dominance:  {(diagnostic.PassedVoteDominance ? "✅" : "❌")}");
			Console.WriteLine($"   Core Strategy:   {(diagnostic.PassedCoreStrategy ? "✅" : "❌")}");
			Console.WriteLine($"   Quality Score:   {(diagnostic.PassedQualityScore ? "✅" : "❌")} ({diagnostic.QualityScore:P0})");

			Console.WriteLine($"\n🚫 BLOCKED AT: {diagnostic.BlockedAt}");
			Console.WriteLine($"   Details: {diagnostic.Details}");

			// If blocked at votes, show what we need
			if (diagnostic.BlockedAt == "INSUFFICIENT_VOTES")
			{
				Console.WriteLine($"\n💡 RECOMMENDATION:");
				Console.WriteLine($"   Current: {Math.Max(diagnostic.BuyVotes, diagnostic.SellVotes)} votes");
				Console.WriteLine($"   Required: 5 votes (with current settings)");
				Console.WriteLine($"   Options:");
				Console.WriteLine($"   1. Lower MIN_VOTES_REQUIRED to 4");
				Console.WriteLine($"   2. Lower MIN_STRATEGY_CONFIDENCE to 0.42");
				Console.WriteLine($"   3. Check if PrimaryFilters is blocking wrong direction");
			}

			if (diagnostic.BlockedAt == "PRIMARY_FILTERS")
			{
				Console.WriteLine($"\n💡 RECOMMENDATION:");
				Console.WriteLine($"   PrimaryFilters is blocking all signals");
				Console.WriteLine($"   Reason: {diagnostic.Details}");
				Console.WriteLine($"   Options:");
				Console.WriteLine($"   1. Lower ADX threshold in PrimaryFilters (from 22 to 18)");
				Console.WriteLine($"   2. Raise Choppiness threshold (from 50 to 55)");
				Console.WriteLine($"   3. Make EMA alignment less strict");
			}
		}
	}
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 4: USAGE EXAMPLE
// ═══════════════════════════════════════════════════════════════════════════

/*
In your main program, run diagnostics first:

var diagnosticResults = new List<SignalBlockageDiagnostic.DiagnosticResult>();

foreach (var symbol in symbols)
{
    var bars = await GetBarsAsync(symbol);
    
    // Run diagnostic
    var diagnostic = SignalBlockageDiagnostic.DiagnoseSignalBlockage(
        symbol, 
        bars.Closes, 
        bars.Highs, 
        bars.Lows, 
        bars.Volumes, 
        bars.Opens);
    
    diagnosticResults.Add(diagnostic);
    
    // Only run full evaluation if diagnostic shows potential
    if (diagnostic.BuyVotes >= 3 || diagnostic.SellVotes >= 3)
    {
        engine.EvaluateAndLog(symbol, bars.Closes, bars.Highs, bars.Lows, 
            bars.Volumes, bars.Opens, bars.LastDate);
    }
}

// Show summary
SignalBlockageDiagnostic.RunBatchDiagnostic(diagnosticResults);
*/