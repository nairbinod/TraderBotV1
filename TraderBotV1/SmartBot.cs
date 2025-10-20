// Ensure you have installed the Alpaca.Markets NuGet package.  
// You can do this by running the following command in the NuGet Package Manager Console:  
// Install-Package Alpaca.Markets  

using Alpaca.Markets;
using TraderBotV1.Data;

namespace TraderBotV1
{
	public class SmartBot
	{
		private readonly Alpaca.Markets.IAlpacaTradingClient _trading;
		private readonly IMarketDataProvider _data;
		private readonly Config _cfg;
		private readonly TradeEngine _engine;
		private readonly SqliteStorage _db;

		public SmartBot(Alpaca.Markets.IAlpacaTradingClient trading, IMarketDataProvider data, Config cfg)
		{
			_trading = trading;
			_data = data;
			_cfg = cfg;
			_engine = new TradeEngine(_trading);
			_db = new SqliteStorage();
		}

		public async Task RunAsync()
		{
			var mode = (_cfg.Mode ?? "Auto").ToLowerInvariant();
			bool backtest = mode is "backtest" or "auto";
			bool live = mode is "live" or "auto" && _cfg.UsePaperWhenLive;

			foreach (var symbol in _cfg.Symbols)
			{
				Console.WriteLine($"\n📈 Fetching bars for {symbol}...");
				var bars = await _data.GetBarsAsync(symbol, _cfg.DaysHistory);
				if (bars.Count == 0) { Console.WriteLine($"⚠️ No bars for {symbol}"); continue; }

				foreach (var b in bars) // persist prices
					_db.InsertPrice(symbol, b.TimestampUtc, b.Open, b.High, b.Low, b.Close, b.Volume);

				await _engine.EvaluateAndMaybeTradeAsync(symbol, bars, _cfg.RiskPercent, backtest, live, _db);
			}
		}
	}
}