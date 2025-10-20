// TradeLogic.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Security.Principal;
using System.Threading.Tasks;
using Alpaca.Markets;
using TraderBotV1;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TraderBotV1
{
	//	Risk per trade = account equity * riskPercent
	//	Dollar risk per share = ATR(Average True Range)
	//	Quantity = floor(Risk per trade / Dollar risk per share)
	//	riskPercent: fraction of account equity to risk per trade(e.g., 0.01 = 1%)

	//	ATR: a measure of volatility(can use latest ATR from Indicators.ATR)

	//	Quantity: number of shares to buy/sell

	public class TradeLogic
	{
		private readonly IAlpacaTradingClient _tradingClient;
		private readonly IAlpacaDataClient _dataClient;

		private const decimal RISK_PERCENT = 0.01m; // 1% of equity per trade

		public TradeLogic(IAlpacaTradingClient tradingClient, IAlpacaDataClient dataClient)
		{
			_tradingClient = tradingClient;
			_dataClient = dataClient;
		}

		public async Task EvaluateAsync(
			string symbol,
			List<decimal> closes,
			List<decimal> highs,
			List<decimal> lows,
			List<decimal> volumes)
		{
			if (closes.Count < 30) return;

			var rsi = Indicators.RSI(closes);
			var (upper, lower) = Indicators.BollingerBands(closes);
			var (macd, sig) = Indicators.MACD(closes);
			var sma = Indicators.SMA(closes, 20);
			var atr = Indicators.ATR(highs, lows, closes);

			var price = closes.Last();
			var rsiNow = rsi.Last();
			var macdNow = macd.Last();
			var sigNow = sig.Last();
			var volAvg = volumes.TakeLast(20).Average();

			bool buySignal =
				rsiNow < 35 &&
				price < lower.Last() &&
				macdNow > sigNow &&
				volumes.Last() > volAvg;

			bool sellSignal =
				rsiNow > 65 &&
				price > upper.Last() &&
				macdNow < sigNow &&
				volumes.Last() > volAvg;

			var account = await _tradingClient.GetAccountAsync();
			var positions = await _tradingClient.ListPositionsAsync();
			var pos = positions.FirstOrDefault(p => p.Symbol == symbol);
			bool hasPosition = pos != null;

			if (buySignal && !hasPosition)
			{
				decimal lastAtr = atr.Last();
				long qty = RiskManager.CalculateOrderQty(account.Equity ?? 0m, lastAtr, RISK_PERCENT);

				var buyOrder = new NewOrderRequest(
					symbol,
					qty,
					OrderSide.Buy,
					OrderType.Market,
					TimeInForce.Day
				);

				Console.WriteLine($"🟢 BUY {symbol} @ {price}, Qty={qty}");
				await _tradingClient.PostOrderAsync(buyOrder);
			}
			else if (sellSignal && hasPosition)
			{
				long sellQty = (long)Math.Abs(pos.Quantity);

				var sellOrder = new NewOrderRequest(
					symbol,
					sellQty,
					OrderSide.Sell,
					OrderType.Market,
					TimeInForce.Day
				);

				Console.WriteLine($"🔴 SELL {symbol} @ {price}, Qty={sellQty}");
				await _tradingClient.PostOrderAsync(sellOrder);
			}
			else
			{
				Console.WriteLine($"⏸ No trade. RSI={rsiNow:F1}, Price={price:F2}");
			}
		}
	}
}
