// Ensure you have installed the Alpaca.Markets NuGet package.  
// You can do this by running the following command in the NuGet Package Manager Console:  
// Install-Package Alpaca.Markets  

using TraderBotV1.Data;

namespace TraderBotV1
{
    public class SmartBot
    {
        private readonly IMarketDataProvider _dataProvider;
        private readonly TradeEngine _engine;
        private readonly SqliteStorage _db;
        private readonly Config _cfg;

        public SmartBot(IMarketDataProvider dataProvider, SqliteStorage db, Config cfg)
        {
            _dataProvider = dataProvider;
            _db = db;
            _cfg = cfg;
            _engine = new TradeEngine(db, cfg.RiskPercent);
        }

        public async Task RunAsync()
        {
            Console.WriteLine("🚀 SmartBot simulation starting...");

            if (_cfg.Symbols == null || _cfg.Symbols.Length == 0)
                _cfg.Symbols = _db.GetActiveSymbols().ToArray();

            foreach (var symbol in _cfg.Symbols)
            {
                try
                {
                    Console.WriteLine($"\n🔍 Fetching {symbol}...");

                    var bars = await _dataProvider.GetBarsAsync(symbol, _cfg.DaysHistory);
                    if (bars is null || bars.Count < 30)
                    {
                        Console.WriteLine($"⚠️ Not enough data for {symbol}");
                        continue;
                    }

                    var closes = bars.Select(b => b.Close).ToList();
                    var highs = bars.Select(b => b.High).ToList();
                    var lows = bars.Select(b => b.Low).ToList();

                    foreach (var b in bars) // persist prices
                        _db.InsertPrice(symbol, b.TimestampUtc, b.Open, b.High, b.Low, b.Close, b.Volume);

                    _engine.EvaluateAndLog(symbol, closes, highs, lows);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error processing {symbol}: {ex.Message}");
                }
            }

            Console.WriteLine("\n✅ Simulation complete — all results logged in SQLite.");
        }
    }
}