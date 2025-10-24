using System;
using System.Collections.Generic;
using System.Linq;
using TraderBotV1.Data;

namespace TraderBotV1
{
	public class TradeEngine
	{
		private readonly SqliteStorage _db;
		private readonly decimal _riskPercent;

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

			// --- Indicators (aligned with new Indicators.cs) ---
			var emaShort = Indicators.EMAList(closes, 9);
			var emaLong = Indicators.EMAList(closes, 21);
			var rsiList = Indicators.RSIList(closes, 14);
			var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
			var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 20, 2);
			var atr = Indicators.ATRList(highs, lows, closes, 14);

			// --- Core Strategies ---
			var s1 = Strategies.EmaRsi(closes, 9, 21, 14);
			var s2 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM, 14);
			var s3 = Strategies.AtrBreakout(closes, highs, lows, atr, 9, 21, 14);
			var s4 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

			// --- Extended Strategies ---
			var s5 = StrategiesExtended.AdxFilter(highs, lows, closes, 14, 25m);
			var s6 = (volumes != null && volumes.Count == closes.Count)
						? StrategiesExtended.VolumeConfirm(closes, volumes, 20, 1.5m)
						: new StrategySignal("Hold", 0m, "no volume data");
			var s7 = StrategiesExtended.CciReversion(highs, lows, closes, 20);
			var s8 = StrategiesExtended.DonchianBreakout(highs, lows, closes, 20);
			var s9 = StrategiesExtended.PivotReversal(highs, lows, closes);
			var s10 = StrategiesExtended.StochRsiReversal(closes, 14, 14, 3, 3);

			// --- Consensus Signals ---
			var allSignals = new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10 };
			int buyVotes = allSignals.Count(s => s.Signal == "Buy");
			int sellVotes = allSignals.Count(s => s.Signal == "Sell");

			string finalSignal = "Hold";
			if (buyVotes > sellVotes && buyVotes >= 3)
				finalSignal = "Buy";
			else if (sellVotes > buyVotes && sellVotes >= 3)
				finalSignal = "Sell";

			// --- Entry Point Calculation ---
			decimal lastClose = closes.Last();
			decimal prevHigh = highs[^2];
			decimal prevLow = lows[^2];
			decimal atrVal = atr.LastOrDefault();

			decimal entry = lastClose;
			if (finalSignal == "Buy")
				entry = Math.Max(lastClose * 1.002m, prevHigh + atrVal * 0.5m);
			else if (finalSignal == "Sell")
				entry = Math.Min(lastClose * 0.998m, prevLow - atrVal * 0.5m);
			entry = Math.Round(entry, 2);

			// --- Risk-based quantity calculation ---
			decimal equity = 100000m; // simulated account equity
			decimal riskValue = equity * _riskPercent;
			decimal stopDistance = atrVal > 0 ? atrVal : Math.Max(1m, lastClose * 0.01m);
			decimal qty = Math.Max(1, Math.Floor(riskValue / stopDistance));

			// --- Logging Section ---
			LogAllSignals(symbol, new Dictionary<string, StrategySignal>
			{
				{ "EMA+RSI", s1 }, { "Bollinger", s2 }, { "ATR", s3 }, { "MACD", s4 },
				{ "ADX", s5 }, { "Volume", s6 }, { "CCI", s7 }, { "Donchian", s8 },
				{ "Pivot", s9 }, { "StochRSI", s10 }
			});

			string reason = finalSignal switch
			{
				"Buy" => $"Triggered BUY (votes={buyVotes}) at entry {entry:F2}",
				"Sell" => $"Triggered SELL (votes={sellVotes}) at entry {entry:F2}",
				_ => "No consensus, HOLD position"
			};

			_db.InsertSignal(symbol, DateTime.UtcNow, "Consensus", finalSignal, reason);
			_db.InsertSignal(symbol, DateTime.UtcNow, "EntryPoint", finalSignal, $"entry={entry:F2}, qty={qty:F0}");

			Console.WriteLine($"📊 {symbol} | BuyVotes={buyVotes}, SellVotes={sellVotes} | {reason}");
			Console.WriteLine($"➡️ Final: {finalSignal} | Qty={qty:F0} | Entry={entry:F2}\n");

			// --- Simulated trade execution (no live API) ---
			if (finalSignal != "Hold")
				SimulateOrder(symbol, finalSignal, qty, entry, reason);
		}

		private void LogAllSignals(string symbol, Dictionary<string, StrategySignal> signals)
		{
			foreach (var (name, s) in signals)
				_db.InsertSignal(symbol, DateTime.UtcNow, name, s.Signal, $"{s.Strength:F2}|{s.Reason}");
		}

		private void SimulateOrder(string symbol, string side, decimal qty, decimal price, string reason)
		{
			Console.WriteLine($"🧩 Simulating {side} {qty} {symbol} @ {price:F2} | {reason}");
			_db.InsertTrade(symbol, DateTime.UtcNow, side, (long)qty, price);
		}
	}
}
