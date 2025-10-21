using Alpaca.Markets;
using TraderBotV1.Data;

namespace TraderBotV1
{
    public class TradeEngine
    {
        private readonly IAlpacaTradingClient _trading;
        private const int EMA_SHORT = 9;
        private const int EMA_LONG = 21;

        public TradeEngine(IAlpacaTradingClient trading)
        {
            _trading = trading;
        }

        public async Task EvaluateAndMaybeTradeAsync(
            string symbol, List<MarketBar> bars, decimal riskPercent, bool backtest, bool live, SqliteStorage db)
        {
            if (bars.Count < 30) { Console.WriteLine($"Not enough data for {symbol}"); return; }

            var closes = bars.Select(b => b.Close).ToList();
            var highs = bars.Select(b => b.High).ToList();
            var lows = bars.Select(b => b.Low).ToList();

            var emaS = Indicators.EMAList(closes, EMA_SHORT);
            var emaL = Indicators.EMAList(closes, EMA_LONG);
            var rsi = Indicators.RSI(closes, 14);
            var (macd, macdSig) = Indicators.MACDSeries(closes);
            var (ub, mid, lb) = Indicators.BollingerBands(closes, 20, 2);
            var atrList = Indicators.ATRList(highs, lows, closes, 14);

            var s1 = Strategies.EmaRsi(closes, emaS, emaL);
            var s2 = Strategies.BollingerMeanReversion(closes, ub, lb);
            var s3 = Strategies.AtrBreakout(closes, atrList);
            var s4 = Strategies.MacdDivergence(closes, macd);
            var final = Strategies.Combine(s1, s2, s3, s4);

            Console.WriteLine($"{symbol}: S1={s1}, S2={s2}, S3={s3}, S4={s4} → {final}");

            db.InsertSignal(symbol, DateTime.UtcNow, "EMA+RSI", s1, $"rsi={rsi:F2}");
            db.InsertSignal(symbol, DateTime.UtcNow, "Bollinger", s2, $"ub/lb");
            db.InsertSignal(symbol, DateTime.UtcNow, "ATR", s3, $"atr={atrList.Last():F2}");
            db.InsertSignal(symbol, DateTime.UtcNow, "MACD", s4, $"macd={macd.Last():F4}");
            db.InsertSignal(symbol, DateTime.UtcNow, "Combined", final, "vote");

            if (backtest && !live) return;

            //var acct = await _trading.GetAccountAsync();
            //var equity = acct.Equity > 0 ? acct.Equity : acct.TradableCash;
            //var atr = atrList.LastOrDefault();
            //var qty = RiskManager.CalculateQty(equity.Value, atr, riskPercent);
            //if (qty <= 0) return;

            var qty = 1; // For testing purpose, assume we always buy/sell 1 share

            //Skip the call for testing 
            //IPosition? pos = null;
            //try { pos = await _trading.GetPositionAsync(symbol); } catch { }
            //bool hasPos = pos != null && Math.Abs(pos.Quantity) > 0;

            bool hasPos = true; // For testing purpose, assume we always have a position

            if (final == "Buy")//&& !hasPos && live)
            {
                //await _trading.PostOrderAsync(new NewOrderRequest(symbol, qty, OrderSide.Buy, OrderType.Market, TimeInForce.Day));
                db.InsertTrade(symbol, DateTime.UtcNow, "Buy", qty, closes.Last());
                Console.WriteLine($"🟢 BUY {symbol} qty={qty}");
            }
            else if (final == "Sell")// && hasPos && live)
            {
                //long sellQty = (long)Math.Abs(pos!.Quantity);
                //await _trading.PostOrderAsync(new NewOrderRequest(symbol, sellQty, OrderSide.Sell, OrderType.Market, TimeInForce.Day));

                var sellQty = 1; // For testing purpose, assume we sell 1 share
                db.InsertTrade(symbol, DateTime.UtcNow, "Sell", sellQty, closes.Last());
                Console.WriteLine($"🔴 SELL {symbol} qty={sellQty}");
            }
            else
            {
                Console.WriteLine("No live trade executed.");
            }
        }
    }
}