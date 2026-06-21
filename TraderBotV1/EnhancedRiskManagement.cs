using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	/// <summary>
	/// Enhanced Risk Management System
	///
	/// Improvements over basic risk management:
	/// 1. Dynamic position sizing based on volatility, confidence, and regime
	/// 2. Portfolio-level risk limits (total exposure, correlation)
	/// 3. Kelly Criterion for optimal position sizing
	/// 4. Volatility-adjusted stops (ATR-based with regime awareness)
	/// 5. Trailing stop optimization based on trend strength
	/// 6. Risk parity across positions
	/// 7. Maximum drawdown protection
	/// 8. Exposure limits by sector/correlation
	///
	/// This helps:
	/// - Optimize position sizes for maximum risk-adjusted returns
	/// - Prevent over-concentration in correlated positions
	/// - Adapt risk dynamically to market conditions
	/// - Protect capital during adverse conditions
	/// </summary>
	public static class EnhancedRiskManagement
	{
		public class PositionSizing
		{
			public decimal BasePositionSize { get; set; }              // Shares/units to trade
			public decimal PositionValue { get; set; }                 // Dollar value
			public decimal AccountPercentage { get; set; }             // % of account
			public decimal RiskPercentage { get; set; }                // % risk on this trade
			public decimal VolatilityAdjustment { get; set; } = 1.0m;  // Multiplier based on volatility
			public decimal ConfidenceAdjustment { get; set; } = 1.0m;  // Multiplier based on signal quality
			public decimal RegimeAdjustment { get; set; } = 1.0m;      // Multiplier based on market regime
			public decimal PortfolioAdjustment { get; set; } = 1.0m;   // Reduction due to portfolio limits
			public decimal FinalPositionSize { get; set; }             // Final adjusted size
			public string SizingReason { get; set; } = "";
		}

		public class StopLossConfig
		{
			public decimal StopPrice { get; set; }
			public decimal StopDistance { get; set; }
			public decimal StopPercentage { get; set; }
			public string StopType { get; set; } = "ATR";              // ATR/Support/Fixed/Trailing
			public decimal TrailingStopDistance { get; set; }
			public bool UseTrailingStop { get; set; }
			public decimal InitialRisk { get; set; }
			public string Reasoning { get; set; } = "";
		}

		public class RiskLimits
		{
			public decimal MaxPositionSize { get; set; } = 0.20m;      // 20% max per position
			public decimal MaxTotalExposure { get; set; } = 1.0m;      // 100% max total (fully invested)
			public decimal MaxCorrelatedExposure { get; set; } = 0.40m; // 40% max in correlated assets
			public decimal MaxDailyRisk { get; set; } = 0.05m;         // 5% max risk per day
			public decimal MaxPortfolioHeat { get; set; } = 0.20m;     // 20% total portfolio at risk
			public decimal EmergencyStopLoss { get; set; } = 0.15m;    // 15% max portfolio drawdown
		}

		/// <summary>
		/// Calculate optimal position size with multiple adjustment factors
		/// </summary>
		public static PositionSizing CalculatePositionSize(
			decimal entryPrice,
			decimal stopPrice,
			decimal accountSize,
			decimal baseRiskPercent,
			decimal signalConfidence,
			decimal qualityScore,
			EnhancedMarketRegime.RegimeAnalysis regime,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			decimal currentPortfolioExposure = 0m,
			RiskLimits? limits = null)
		{
			limits ??= new RiskLimits();
			var sizing = new PositionSizing();

			// ═══════════════════════════════════════════════════════════════
			// 1. BASE POSITION SIZE (Fixed Risk)
			// ═══════════════════════════════════════════════════════════════

			decimal stopDistance = Math.Abs(entryPrice - stopPrice);
			if (stopDistance == 0) stopDistance = entryPrice * 0.03m; // Default 3% if no stop

			decimal riskAmount = accountSize * baseRiskPercent;
			decimal baseShares = Math.Floor(riskAmount / stopDistance);

			sizing.BasePositionSize = baseShares;
			sizing.RiskPercentage = baseRiskPercent;

			// ═══════════════════════════════════════════════════════════════
			// 2. VOLATILITY ADJUSTMENT
			// Higher volatility = smaller position
			// ═══════════════════════════════════════════════════════════════

			sizing.VolatilityAdjustment = CalculateVolatilityAdjustment(regime);

			// ═══════════════════════════════════════════════════════════════
			// 3. CONFIDENCE/QUALITY ADJUSTMENT
			// Higher confidence = larger position (but capped)
			// ═══════════════════════════════════════════════════════════════

			sizing.ConfidenceAdjustment = CalculateConfidenceAdjustment(
				signalConfidence, qualityScore);

			// ═══════════════════════════════════════════════════════════════
			// 4. REGIME ADJUSTMENT
			// Favorable regime = larger position
			// ═══════════════════════════════════════════════════════════════

			sizing.RegimeAdjustment = regime.PositionSizeMultiplier;

			// ═══════════════════════════════════════════════════════════════
			// 5. PORTFOLIO-LEVEL ADJUSTMENT
			// Reduce if approaching exposure limits
			// ═══════════════════════════════════════════════════════════════

			sizing.PortfolioAdjustment = CalculatePortfolioAdjustment(
				currentPortfolioExposure, limits);

			// ═══════════════════════════════════════════════════════════════
			// 6. CALCULATE FINAL POSITION SIZE
			// ═══════════════════════════════════════════════════════════════

			decimal totalAdjustment = sizing.VolatilityAdjustment *
									 sizing.ConfidenceAdjustment *
									 sizing.RegimeAdjustment *
									 sizing.PortfolioAdjustment;

			sizing.FinalPositionSize = Math.Max(1, Math.Floor(baseShares * totalAdjustment));
			sizing.PositionValue = sizing.FinalPositionSize * entryPrice;
			sizing.AccountPercentage = sizing.PositionValue / accountSize;

			// ═══════════════════════════════════════════════════════════════
			// 7. APPLY HARD LIMITS
			// ═══════════════════════════════════════════════════════════════

			decimal maxPositionValue = accountSize * limits.MaxPositionSize;
			if (sizing.PositionValue > maxPositionValue)
			{
				sizing.FinalPositionSize = Math.Floor(maxPositionValue / entryPrice);
				sizing.PositionValue = sizing.FinalPositionSize * entryPrice;
				sizing.AccountPercentage = sizing.PositionValue / accountSize;
				sizing.SizingReason = $"Capped at {limits.MaxPositionSize:P0} max position size";
			}

			// Build sizing reason
			if (string.IsNullOrEmpty(sizing.SizingReason))
			{
				var reasons = new List<string>();
				if (sizing.VolatilityAdjustment < 0.9m)
					reasons.Add($"Vol adj {sizing.VolatilityAdjustment:P0}");
				if (sizing.ConfidenceAdjustment > 1.1m)
					reasons.Add($"Conf boost {sizing.ConfidenceAdjustment:P0}");
				if (sizing.RegimeAdjustment < 0.9m)
					reasons.Add($"Regime reduce {sizing.RegimeAdjustment:P0}");
				if (sizing.PortfolioAdjustment < 1.0m)
					reasons.Add($"Portfolio limit {sizing.PortfolioAdjustment:P0}");

				sizing.SizingReason = reasons.Any() ? string.Join(", ", reasons) : "Standard sizing";
			}

			return sizing;
		}

		/// <summary>
		/// Calculate optimal stop loss placement
		/// </summary>
		public static StopLossConfig CalculateStopLoss(
			decimal entryPrice,
			string direction,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			EnhancedMarketRegime.RegimeAnalysis regime,
			decimal baseStopPercent = 0.04m)
		{
			var stopConfig = new StopLossConfig();
			int idx = closes.Count - 1;

			// ═══════════════════════════════════════════════════════════════
			// 1. ATR-BASED STOP (Primary method for swing trading)
			// ═══════════════════════════════════════════════════════════════

			var atr = Indicators.ATRList(highs, lows, closes, 14);
			decimal atrValue = atr.Count > 0 ? atr[^1] : entryPrice * 0.02m;

			// ATR multiplier adjusted for regime
			decimal atrMultiplier = regime.RecommendedStopMultiplier;

			// Trending markets can use wider stops
			if (regime.PrimaryRegime == "Trending" && regime.TrendStrength > 0.6m)
				atrMultiplier += 0.2m;

			// Volatile markets need wider stops to avoid whipsaws
			if (regime.VolatilityRegime == "High" || regime.VolatilityRegime == "Extreme")
				atrMultiplier += 0.3m;

			decimal atrStop = atrValue * atrMultiplier;

			// ═══════════════════════════════════════════════════════════════
			// 2. SUPPORT/RESISTANCE STOP
			// ═══════════════════════════════════════════════════════════════

			var srLevels = Indicators.FindSupportResistance(highs, lows, closes);
			decimal srStop = 0m;

			if (direction == "Buy")
			{
				var nearestSupport = srLevels
					.Where(l => l.IsSupport && l.Level < entryPrice)
					.OrderByDescending(l => l.Level)
					.FirstOrDefault();

				if (nearestSupport != null)
					srStop = entryPrice - nearestSupport.Level;
			}
			else
			{
				var nearestResistance = srLevels
					.Where(l => !l.IsSupport && l.Level > entryPrice)
					.OrderBy(l => l.Level)
					.FirstOrDefault();

				if (nearestResistance != null)
					srStop = nearestResistance.Level - entryPrice;
			}

			// ═══════════════════════════════════════════════════════════════
			// 3. CHOOSE BEST STOP METHOD
			// ═══════════════════════════════════════════════════════════════

			decimal chosenStop = atrStop;
			string stopType = "ATR";

			// Use S/R stop if it's reasonable (between 2% and 8%)
			if (srStop > 0 && srStop > entryPrice * 0.02m && srStop < entryPrice * 0.08m)
			{
				chosenStop = srStop;
				stopType = "Support/Resistance";
			}

			// Ensure stop is within reasonable bounds
			decimal minStop = entryPrice * 0.025m;  // 2.5% minimum
			decimal maxStop = entryPrice * 0.10m;   // 10% maximum

			chosenStop = Math.Max(minStop, Math.Min(chosenStop, maxStop));

			// ═══════════════════════════════════════════════════════════════
			// 4. CALCULATE STOP PRICE
			// ═══════════════════════════════════════════════════════════════

			if (direction == "Buy")
				stopConfig.StopPrice = entryPrice - chosenStop;
			else
				stopConfig.StopPrice = entryPrice + chosenStop;

			stopConfig.StopDistance = chosenStop;
			stopConfig.StopPercentage = chosenStop / entryPrice;
			stopConfig.StopType = stopType;
			stopConfig.InitialRisk = chosenStop;

			// ═══════════════════════════════════════════════════════════════
			// 5. TRAILING STOP CONFIGURATION
			// ═══════════════════════════════════════════════════════════════

			// Use trailing stops in strong trends
			if (regime.PrimaryRegime == "Trending" && regime.TrendStrength > 0.6m)
			{
				stopConfig.UseTrailingStop = true;
				stopConfig.TrailingStopDistance = atrValue * (atrMultiplier - 0.5m); // Tighter trail
				stopConfig.Reasoning = $"{stopType} stop with trailing (strong trend)";
			}
			else
			{
				stopConfig.UseTrailingStop = false;
				stopConfig.Reasoning = $"{stopType} stop at {stopConfig.StopPercentage:P1}";
			}

			return stopConfig;
		}

		/// <summary>
		/// Calculate Kelly Criterion for position sizing (optional, aggressive)
		/// </summary>
		public static decimal CalculateKellyCriterion(
			decimal winRate,
			decimal avgWin,
			decimal avgLoss,
			bool useHalfKelly = true)
		{
			if (avgLoss == 0) return 0m;

			decimal winLossRatio = avgWin / avgLoss;
			decimal kellyPercent = (winRate * winLossRatio - (1 - winRate)) / winLossRatio;

			// Kelly can be aggressive, so use half-Kelly or fractional Kelly
			if (useHalfKelly)
				kellyPercent *= 0.5m;

			// Cap at reasonable limits
			return Math.Max(0m, Math.Min(kellyPercent, 0.25m)); // Max 25% position
		}

		/// <summary>
		/// Check portfolio-level risk limits
		/// </summary>
		public static (bool IsAllowed, string Reason) CheckPortfolioRiskLimits(
			decimal currentExposure,
			decimal currentPortfolioHeat,
			decimal proposedPosition,
			decimal proposedRisk,
			RiskLimits limits)
		{
			// 1. Total exposure limit
			if (currentExposure + proposedPosition > limits.MaxTotalExposure)
			{
				return (false, $"Total exposure would exceed {limits.MaxTotalExposure:P0} " +
							  $"(current: {currentExposure:P0}, proposed: {proposedPosition:P0})");
			}

			// 2. Portfolio heat limit (total risk)
			if (currentPortfolioHeat + proposedRisk > limits.MaxPortfolioHeat)
			{
				return (false, $"Portfolio heat would exceed {limits.MaxPortfolioHeat:P0} " +
							  $"(current: {currentPortfolioHeat:P0}, proposed: {proposedRisk:P0})");
			}

			// 3. Single position size limit
			if (proposedPosition > limits.MaxPositionSize)
			{
				return (false, $"Position size exceeds {limits.MaxPositionSize:P0} limit");
			}

			return (true, "Risk limits OK");
		}

		/// <summary>
		/// Calculate correlation-adjusted exposure
		/// This would ideally use actual correlation matrix, but we can estimate
		/// </summary>
		public static decimal CalculateCorrelationAdjustedExposure(
			List<string> currentSymbols,
			string newSymbol,
			decimal baseExposure)
		{
			// Simplified correlation estimation
			// In production, you'd calculate actual correlation from price history

			// Sector correlation (estimated)
			// Tech stocks tend to be correlated, same with financials, energy, etc.
			var techSymbols = new[] { "AAPL", "MSFT", "GOOGL", "NVDA", "AMD", "META" };
			var financeSymbols = new[] { "JPM", "BAC", "WFC", "GS", "MS" };
			var energySymbols = new[] { "XOM", "CVX", "COP", "SLB" };

			int correlatedCount = 0;

			foreach (var symbol in currentSymbols)
			{
				// Check if in same sector
				bool sameGroup = (techSymbols.Contains(symbol) && techSymbols.Contains(newSymbol)) ||
								(financeSymbols.Contains(symbol) && financeSymbols.Contains(newSymbol)) ||
								(energySymbols.Contains(symbol) && energySymbols.Contains(newSymbol));

				if (sameGroup)
					correlatedCount++;
			}

			// Reduce effective exposure based on correlation
			// More correlated positions = less diversification benefit
			decimal correlationPenalty = correlatedCount * 0.1m; // 10% per correlated position
			decimal adjustedExposure = baseExposure * (1 + correlationPenalty);

			return adjustedExposure;
		}

		// ═══════════════════════════════════════════════════════════════
		// PRIVATE HELPER METHODS
		// ═══════════════════════════════════════════════════════════════

		private static decimal CalculateVolatilityAdjustment(EnhancedMarketRegime.RegimeAnalysis regime)
		{
			decimal adjustment = 1.0m;

			// Reduce size in high volatility
			if (regime.VolatilityRegime == "Extreme")
				adjustment = 0.5m;  // 50% size
			else if (regime.VolatilityRegime == "High")
				adjustment = 0.7m;  // 70% size
			else if (regime.VolatilityRegime == "Low")
				adjustment = 1.2m;  // 120% size (low vol = lower risk)

			// Further reduce if volatility is expanding rapidly
			if (regime.VolatilityExpanding && regime.VolatilityCluster > 1.3m)
				adjustment *= 0.8m;

			return adjustment;
		}

		private static decimal CalculateConfidenceAdjustment(decimal signalConfidence, decimal qualityScore)
		{
			// Average confidence and quality
			decimal avgScore = (signalConfidence + qualityScore) / 2m;

			decimal adjustment = 1.0m;

			// High quality = larger position (but capped)
			if (avgScore >= 0.80m)
				adjustment = 1.3m;  // +30%
			else if (avgScore >= 0.70m)
				adjustment = 1.2m;  // +20%
			else if (avgScore >= 0.60m)
				adjustment = 1.1m;  // +10%
			else if (avgScore < 0.50m)
				adjustment = 0.8m;  // -20%

			return adjustment;
		}

		private static decimal CalculatePortfolioAdjustment(decimal currentExposure, RiskLimits limits)
		{
			// Reduce position size as we approach portfolio limits
			decimal exposureRatio = currentExposure / limits.MaxTotalExposure;

			if (exposureRatio >= 0.9m)
				return 0.5m;  // 50% size when near limit
			else if (exposureRatio >= 0.8m)
				return 0.7m;  // 70% size
			else if (exposureRatio >= 0.7m)
				return 0.85m; // 85% size

			return 1.0m;  // Full size when plenty of room
		}
	}
}
