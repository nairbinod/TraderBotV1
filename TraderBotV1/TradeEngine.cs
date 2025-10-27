using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	public class TradeEngine
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;
		private const int MIN_VOTES_REQUIRED = 3; // Require 3+ confirmations
		private const decimal MIN_CONFIDENCE = 0.6m; // Minimum 60% confidence

		public TradeEngine(SqliteStorage db, decimal riskPercent = 0.01m)
		{
			_db = db;
			_riskPercent = riskPercent;
		}

		public void EvaluateAndLog(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal>? volumes = null)
		{
			if (closes.Count < 60)
			{
				Console.WriteLine($"⚠️ Not enough data for {symbol}");
				return;
			}

			// --- Calculate Indicators (clean, standard) ---
			var emaShort = Indicators.EMAList(closes, 9);
			var emaLong = Indicators.EMAList(closes, 21);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 20, 2);
			var atr = Indicators.ATRList(highs, lows, closes, 14);

			int idx = closes.Count - 1;
			var context = SignalValidator.AnalyzeMarketContext(closes, highs, lows, idx);

			// --- Market Context Check ---
			Console.WriteLine($"\n📊 {symbol} Market Context:");
			Console.WriteLine($"   Volatility: {context.RecentVolatility:F4} (ratio: {context.VolatilityRatio:F2}x)");
			Console.WriteLine($"   Range: {context.RecentRange:P2}");
			Console.WriteLine($"   Trend: {(context.IsUptrend ? "UP" : context.IsDowntrend ? "DOWN" : "SIDEWAYS")} (strength: {context.TrendStrength:P2})");

			// Skip trading in extreme conditions
			if (context.VolatilityRatio > 3m)
			{
				Console.WriteLine($"⚠️ Extreme volatility spike - skipping {symbol}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Context", "Hold", "Extreme volatility");
				return;
			}

			if (context.RecentRange < 0.005m)
			{
				Console.WriteLine($"⚠️ Very low volatility - skipping {symbol}");
				_db.InsertSignal(symbol, DateTime.UtcNow, "Context", "Hold", "Low volatility");
				return;
			}

			// --- Execute Strategies (with built-in validation) ---
			var s1 = Strategies.EmaRsi(closes, 9, 21, 14);
			var s2 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s3 = Strategies.AtrBreakout(closes, highs, lows, atr, 9, 21, 14);
			var s4 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

			var s5 = StrategiesExtended.AdxFilter(highs, lows, closes, 14, 25m);
			var s6 = (volumes != null && volumes.Count == closes.Count)
						? StrategiesExtended.VolumeConfirm(closes, volumes, 20, 1.5m)
						: new StrategySignal("Hold", 0m, "no volume data");
			var s7 = StrategiesExtended.CciReversion(highs, lows, closes, 20);
			var s8 = StrategiesExtended.DonchianBreakout(highs, lows, closes, 20);
			var s9 = StrategiesExtended.PivotReversal(highs, lows, closes);
			var s10 = StrategiesExtended.StochRsiReversal(closes, 14, 14, 3, 3);

			// --- Weighted Consensus Analysis ---
			var allSignals = new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10 };

			decimal buyScore = 0m;
			decimal sellScore = 0m;
			int buyVotes = 0;
			int sellVotes = 0;

			foreach (var signal in allSignals)
			{
				if (signal.Signal == "Buy" && signal.Strength >= MIN_CONFIDENCE)
				{
					buyVotes++;
					buyScore += signal.Strength;
				}
				else if (signal.Signal == "Sell" && signal.Strength >= MIN_CONFIDENCE)
				{
					sellVotes++;
					sellScore += signal.Strength;
				}
			}

			// Calculate average confidence
			decimal avgBuyConfidence = buyVotes > 0 ? buyScore / buyVotes : 0m;
			decimal avgSellConfidence = sellVotes > 0 ? sellScore / sellVotes : 0m;

			// --- Multi-Indicator Confirmation Check ---
			bool requireExtra = SignalValidator.RequiresMultiIndicatorConfirmation(context);
			int requiredVotes = requireExtra ? MIN_VOTES_REQUIRED + 1 : MIN_VOTES_REQUIRED;

			Console.WriteLine($"\n🔍 Signal Analysis:");
			Console.WriteLine($"   Buy votes: {buyVotes} (avg confidence: {avgBuyConfidence:P0})");
			Console.WriteLine($"   Sell votes: {sellVotes} (avg confidence: {avgSellConfidence:P0})");
			Console.WriteLine($"   Required votes: {requiredVotes} {(requireExtra ? "(choppy market)" : "")}");

			// --- Final Decision ---
			string finalSignal = "Hold";
			decimal finalConfidence = 0m;
			string finalReason = "No consensus";

			if (buyVotes >= requiredVotes && buyVotes > sellVotes && avgBuyConfidence >= MIN_CONFIDENCE)
			{
				finalSignal = "Buy";
				finalConfidence = avgBuyConfidence;
				finalReason = $"{buyVotes} strategies confirmed (conf: {avgBuyConfidence:P0})";
			}
			else if (sellVotes >= requiredVotes && sellVotes > buyVotes && avgSellConfidence >= MIN_CONFIDENCE)
			{
				finalSignal = "Sell";
				finalConfidence = avgSellConfidence;
				finalReason = $"{sellVotes} strategies confirmed (conf: {avgSellConfidence:P0})";
			}
			else if (buyVotes > 0 || sellVotes > 0)
			{
				finalReason = $"Insufficient confirmation (buy:{buyVotes}, sell:{sellVotes}, need:{requiredVotes})";
			}

			// --- Entry Point Calculation ---
			decimal lastClose = closes[idx];
			decimal atrVal = atr.LastOrDefault();
			decimal entry = lastClose;
			decimal stopDistance = atrVal > 0 ? atrVal * 1.5m : lastClose * 0.02m;

			if (finalSignal == "Buy")
			{
				// Conservative entry: wait for slight pullback or confirmation
				entry = Math.Round(lastClose * 1.001m, 2); // 0.1% above
				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, 1, entry);
			}
			else if (finalSignal == "Sell")
			{
				entry = Math.Round(lastClose * 0.999m, 2); // 0.1% below
				_db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, 1, entry);
			}

			// --- Position Sizing (risk-based) ---
			decimal equity = 100000m; // Simulated account
			decimal riskValue = equity * _riskPercent;
			decimal qty = Math.Max(1, Math.Floor(riskValue / stopDistance));

			// --- Detailed Logging ---
			LogAllSignals(symbol, new Dictionary<string, StrategySignal>
			{
				{ "EMA+RSI", s1 }, { "Bollinger", s2 }, { "ATR", s3 }, { "MACD", s4 },
				{ "ADX", s5 }, { "Volume", s6 }, { "CCI", s7 }, { "Donchian", s8 },
				{ "Pivot", s9 }, { "StochRSI", s10 }
			});

			_db.InsertSignal(symbol, DateTime.UtcNow, "Consensus", finalSignal,
				$"{finalReason} | conf={finalConfidence:P0}");

			if (finalSignal != "Hold")
			{
				_db.InsertSignal(symbol, DateTime.UtcNow, "EntryPoint", finalSignal,
					$"entry=${entry:F2}, qty={qty:F0}, stop=${stopDistance:F2}");
			}

			// --- Console Output ---
			Console.WriteLine($"\n{'✅'} {symbol} FINAL DECISION:");
			Console.WriteLine($"   Signal: {finalSignal}");
			Console.WriteLine($"   Confidence: {finalConfidence:P0}");
			Console.WriteLine($"   Reason: {finalReason}");

			//if (finalSignal != "Hold")
			//{
			//	Console.WriteLine($"   Entry: ${entry:F2}");
			//	Console.WriteLine($"   Quantity: {qty:F0}");
			//	Console.WriteLine($"   Stop Distance: ${stopDistance:F2}");
			//	Console.WriteLine($"   Risk: ${riskValue:F2} ({_riskPercent:P1} of equity)");

			//	SimulateOrder(symbol, finalSignal, qty, entry, finalReason);
			//}
			//else
			//{
			//	Console.WriteLine($"   Action: HOLD - {finalReason}");
			//}
		}

		private void LogAllSignals(string symbol, Dictionary<string, StrategySignal> signals)
		{
			Console.WriteLine($"\n📋 Individual Strategy Signals:");
			foreach (var (name, s) in signals)
			{
				string icon = s.Signal == "Buy" ? "🟢" : s.Signal == "Sell" ? "🔴" : "⚪";
				Console.WriteLine($"   {icon} {name,-12}: {s.Signal,-4} ({s.Strength:P0}) - {s.Reason}");

				_db.InsertSignal(symbol, DateTime.UtcNow, name, s.Signal,
					$"{s.Strength:F2}|{s.Reason}");
			}
		}

		private void SimulateOrder(string symbol, string side, decimal qty, decimal price, string reason)
		{
			Console.WriteLine($"\n🎯 SIMULATED ORDER:");
			Console.WriteLine($"   Symbol: {symbol}");
			Console.WriteLine($"   Side: {side}");
			Console.WriteLine($"   Quantity: {qty:F0}");
			Console.WriteLine($"   Price: ${price:F2}");
			Console.WriteLine($"   Total Value: ${qty * price:F2}");
			Console.WriteLine($"   Reason: {reason}");

			_db.InsertTrade(symbol, DateTime.UtcNow, side, (long)qty, price);
		}
	}
}