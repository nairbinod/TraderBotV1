using TraderBotV1.Data;

namespace TraderBotV1
{
    //public class TradeEngine
    //{
    //    private readonly IAlpacaTradingClient _trading;
    //    private const int EMA_SHORT = 9;
    //    private const int EMA_LONG = 21;

    //    public TradeEngine(IAlpacaTradingClient trading)
    //    {
    //        _trading = trading;
    //    }

    //    public async Task EvaluateAndMaybeTradeAsync(
    //        string symbol, List<MarketBar> bars, decimal riskPercent, bool backtest, bool live, SqliteStorage db)
    //    {
    //        if (bars.Count < 30) { Console.WriteLine($"Not enough data for {symbol}"); return; }

    //        var closes = bars.Select(b => b.Close).ToList();
    //        var highs = bars.Select(b => b.High).ToList();
    //        var lows = bars.Select(b => b.Low).ToList();

    //        var emaS = Indicators.EMAList(closes, EMA_SHORT);
    //        var emaL = Indicators.EMAList(closes, EMA_LONG);
    //        var rsi = Indicators.RSI(closes, 14);
    //        var (macd, macdSig) = Indicators.MACDSeries(closes);
    //        var (ub, mid, lb) = Indicators.BollingerBands(closes, 20, 2);
    //        var atrList = Indicators.ATRList(highs, lows, closes, 14);

    //        var s1 = Strategies.EmaRsi(closes, emaS, emaL);
    //        var s2 = Strategies.BollingerMeanReversion(closes, ub, lb);
    //        var s3 = Strategies.AtrBreakout(closes, atrList);
    //        var s4 = Strategies.MacdDivergence(closes, macd);
    //        var final = Strategies.Combine(s1, s2, s3, s4);

    //        Console.WriteLine($"{symbol}: S1={s1}, S2={s2}, S3={s3}, S4={s4} → {final}");

    //        db.InsertSignal(symbol, DateTime.UtcNow, "EMA+RSI", s1, $"rsi={rsi:F2}");
    //        db.InsertSignal(symbol, DateTime.UtcNow, "Bollinger", s2, $"ub/lb={ub.Last():F2}/{lb.Last():F2}");
    //        db.InsertSignal(symbol, DateTime.UtcNow, "ATR", s3, $"atr={atrList.Last():F2}");
    //        db.InsertSignal(symbol, DateTime.UtcNow, "MACD", s4, $"macd={macd.Last():F4}");
    //        db.InsertSignal(symbol, DateTime.UtcNow, "Combined", final, $"vote={closes.Last():F2}");

    //        if (backtest && !live) return;

    //        //var acct = await _trading.GetAccountAsync();
    //        //var equity = acct.Equity > 0 ? acct.Equity : acct.TradableCash;
    //        //var atr = atrList.LastOrDefault();
    //        //var qty = RiskManager.CalculateQty(equity.Value, atr, riskPercent);
    //        //if (qty <= 0) return;

    //        var qty = 1; // For testing purpose, assume we always buy/sell 1 share

    //        //Skip the call for testing 
    //        //IPosition? pos = null;
    //        //try { pos = await _trading.GetPositionAsync(symbol); } catch { }
    //        //bool hasPos = pos != null && Math.Abs(pos.Quantity) > 0;

    //        bool hasPos = true; // For testing purpose, assume we always have a position

    //        if (final == "Buy")//&& !hasPos && live)
    //        {
    //            //await _trading.PostOrderAsync(new NewOrderRequest(symbol, qty, OrderSide.Buy, OrderType.Market, TimeInForce.Day));
    //            db.InsertTrade(symbol, DateTime.UtcNow, "Buy", qty, closes.Last());
    //            Console.WriteLine($"🟢 BUY {symbol} qty={qty}");
    //        }
    //        else if (final == "Sell")// && hasPos && live)
    //        {
    //            //long sellQty = (long)Math.Abs(pos!.Quantity);
    //            //await _trading.PostOrderAsync(new NewOrderRequest(symbol, sellQty, OrderSide.Sell, OrderType.Market, TimeInForce.Day));

    //            var sellQty = 1; // For testing purpose, assume we sell 1 share
    //            db.InsertTrade(symbol, DateTime.UtcNow, "Sell", sellQty, closes.Last());
    //            Console.WriteLine($"🔴 SELL {symbol} qty={sellQty}");
    //        }
    //        else
    //        {
    //            Console.WriteLine("No live trade executed.");
    //        }
    //    }
    //}

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
            List<decimal> lows, List<decimal>? volumes = null)
        {
            if (closes.Count < 50)
            {
                Console.WriteLine($"⚠️ Not enough data for {symbol}");
                return;
            }

            // --- Compute all indicators ---
            var emaShort = Indicators.EMAList(closes, 9);
            var emaLong = Indicators.EMAList(closes, 21);
            var rsi = Indicators.RSI(closes, 14);
            var (macd, macdSig, macdHist) = Indicators.MACDSeries(closes);
            var (bbU, bbM, bbL) = Indicators.BollingerBandsFast(closes, 20, 2);
            var atr = Indicators.ATRList(highs, lows, closes, 14);

            // --- Run strategies ---
            var s1 = Strategies.EmaRsi(closes, emaShort, emaLong);
            var s2 = Strategies.BollingerMeanReversion(closes, bbU, bbL, bbM);
            var s3 = Strategies.AtrBreakout(closes, highs, lows, atr, 0.5m);
            var s4 = Strategies.MacdDivergence(closes, macd, macdSig, macdHist);

            // --- Run extended strategies (equal voting) ---
            var s5 = StrategiesExtended.AdxFilter(highs, lows, closes, 14, 25m);
            var s6 = (volumes != null && volumes.Count == closes.Count)
                        ? StrategiesExtended.VolumeConfirm(closes, volumes, 20, 1.5m)
                        : new StrategySignal("Hold", 0m, "no volumes");
            var s7 = StrategiesExtended.CciReversion(highs, lows, closes, 20);
            var s8 = StrategiesExtended.DonchianBreakout(highs, lows, closes, 20);
            var s9 = StrategiesExtended.PivotReversal(highs, lows, closes);
            var s10 = StrategiesExtended.StochRsiReversal(closes, 14, 14, 3, 3);
            var s11 = StrategiesExtended.Ema200RegimeFilter(closes, 200);


            // --- Log all individual signals ---
            _db.InsertSignal(symbol, DateTime.UtcNow, "EMA+RSI", s1.Signal, $"{s1.Strength:F2}|{s1.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "Bollinger", s2.Signal, $"{s2.Strength:F2}|{s2.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "ATR BO", s3.Signal, $"{s3.Strength:F2}|{s3.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "MACD Div", s4.Signal, $"{s4.Strength:F2}|{s4.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "ADX", s5.Signal, $"{s5.Strength:F2}|{s5.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "Volume", s6.Signal, $"{s6.Strength:F2}|{s6.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "CCI", s7.Signal, $"{s7.Strength:F2}|{s7.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "Donchian", s8.Signal, $"{s8.Strength:F2}|{s8.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "Pivot", s9.Signal, $"{s9.Strength:F2}|{s9.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "StochRSI", s10.Signal, $"{s10.Strength:F2}|{s10.Reason}");
            _db.InsertSignal(symbol, DateTime.UtcNow, "EMA200", s11.Signal, $"{s11.Strength:F2}|{s11.Reason}");

            // --- Majority vote logic ---
            var allSignals = new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11 };

            var buyVotes = allSignals.Count(s => s.Signal == "Buy");
            var sellVotes = allSignals.Count(s => s.Signal == "Sell");

            string finalSignal = "Hold";
            List<string> agreeing = new();

            if (buyVotes >= 2)
            {
                finalSignal = "Buy";
                agreeing = allSignals.Where(s => s.Signal == "Buy").Select(s => GetShortName(s)).ToList();
                _db.InsertTrade(symbol, DateTime.UtcNow, finalSignal, 1, closes.Last());
            }
            else if (sellVotes >= 2)
            {
                finalSignal = "Sell";
                agreeing = allSignals.Where(s => s.Signal == "Sell").Select(s => GetShortName(s)).ToList();
            }

            // --- Entry Price Logic ---
            // --- Entry Price Logic (breakout-style with ATR buffer) ---
            var close = closes[^1];
            var prevHigh = highs[^2];
            var prevLow = lows[^2];
            var atrVal = atr.LastOrDefault();

            decimal entry = close;
            if (finalSignal == "Buy") entry = Math.Max(close * 1.002m, prevHigh + atrVal * 0.5m);
            if (finalSignal == "Sell") entry = Math.Min(close * 0.998m, prevLow - atrVal * 0.5m);
            entry = Math.Round(entry, 2);

            // --- Risk-based quantity ---
            decimal accountEquity = 100000m; // simulated equity
            decimal riskAmount = accountEquity * _riskPercent;
            decimal stopDistance = atrVal > 0 ? atrVal : Math.Max(1m, close * 0.01m);
            decimal qty = Math.Max(1, Math.Floor(riskAmount / stopDistance));

            // --- Log consensus & entry ---
            string reason = (finalSignal != "Hold" && agreeing.Count >= 2)
                ? $"Triggered {finalSignal} by {string.Join(" + ", agreeing)}"
                : "No consensus (holding)";

            _db.InsertSignal(symbol, DateTime.UtcNow, "Consensus", finalSignal, reason);
            _db.InsertSignal(symbol, DateTime.UtcNow, "EntryPoint", finalSignal, $"entry={entry:F2}");

            Console.WriteLine($"📊 {symbol} | BuyVotes={buyVotes}, SellVotes={sellVotes} | {reason}");
            Console.WriteLine($"📈 {symbol} | Final={finalSignal} | Entry={entry:F2}");

            // --- Simulate trade execution (no live orders) ---
            //if (finalSignal == "Buy" && buyVotes >= 2)
            //    SimulateOrder(symbol, "Buy", qty, entry, reason);
            //else if (finalSignal == "Sell" && sellVotes >= 2)
            //    SimulateOrder(symbol, "Sell", qty, entry, reason);
            //else
            //    Console.WriteLine($"    → Holding {symbol}, no clear consensus.\n");
        }

        private void SimulateOrder(string symbol, string side, decimal qty, decimal price, string reason)
        {
            Console.WriteLine($"🧩 Simulating {side} {qty} {symbol} @ {price:F2} | {reason}");
            _db.InsertTrade(symbol, DateTime.UtcNow, side, (long)qty, price);
        }

        private static string GetShortName(StrategySignal s)
        {
            // match by reason keywords (keeps it simple and avoids changing StrategySignal shape)
            var r = s.Reason ?? "";
            if (r.Contains("EMA", StringComparison.OrdinalIgnoreCase)) return "EMA+RSI";
            if (r.Contains("LB") || r.Contains("UB") || r.Contains("bands")) return "BBands";
            if (r.Contains("breakout") || r.Contains("breakdown") || r.Contains("ATR")) return "ATR BO";
            if (r.Contains("div", StringComparison.OrdinalIgnoreCase)) return "MACD Div";
            if (r.Contains("ADX", StringComparison.OrdinalIgnoreCase)) return "ADX";
            if (r.Contains("Vol spike", StringComparison.OrdinalIgnoreCase)) return "Volume";
            if (r.Contains("CCI", StringComparison.OrdinalIgnoreCase)) return "CCI";
            if (r.Contains("Donchian", StringComparison.OrdinalIgnoreCase)) return "Donchian";
            if (r.Contains("R1") || r.Contains("S1") || r.Contains("Pivot", StringComparison.OrdinalIgnoreCase)) return "Pivot";
            if (r.Contains("StochRSI", StringComparison.OrdinalIgnoreCase)) return "StochRSI";
            if (r.Contains("Regime", StringComparison.OrdinalIgnoreCase)) return "EMA200";
            return "Unknown";
        }
    }
}
