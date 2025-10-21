namespace TraderBotV1
{

    /*
        RSI (14) below 30 → Oversold → Buy. Uses 14-day period.
        EMA (9) crosses above EMA (21) → Bullish crossover → Buy.
        MACD(12,26,9) shows bullish divergence → Buy.
        Bollinger Bands (20,2) price touches lower band → Buy.
        ATR (14) breakout above previous high + ATR → Buy.
        
        Four Strategies:
        1. EMA + RSI Strategy
        2. Bollinger Bands Mean Reversion
        3. ATR Breakout Strategy
        4. MACD Divergence Strategy
        Combine signals: If 2 or more strategies signal Buy, then Buy. If 2 or more signal Sell, then Sell. Else Hold.

    */

    public static class Strategies
    {
        public static string EmaRsi(List<decimal> closes, List<decimal> emaShort, List<decimal> emaLong)
        {
            if (emaShort.Count < 2 || emaLong.Count < 2) return "Hold";
            bool crossUp = emaShort[^1] > emaLong[^1] && emaShort[^2] <= emaLong[^2];
            bool crossDown = emaShort[^1] < emaLong[^1] && emaShort[^2] >= emaLong[^2];
            var rsi = Indicators.RSI(closes);
            if (crossUp && rsi > 30 && rsi < 75) return "Buy";
            if (crossDown && rsi < 70 && rsi > 25) return "Sell";
            return "Hold";
        }

        public static string BollingerMeanReversion(List<decimal> closes, List<decimal> upper, List<decimal> lower)
        {
            var price = closes.Last();
            if (price <= lower.Last()) return "Buy";
            if (price >= upper.Last()) return "Sell";
            return "Hold";
        }

        public static string AtrBreakout(List<decimal> closes, List<decimal> atr)
        {
            if (closes.Count < 2 || atr.Count == 0) return "Hold";
            var last = closes[^1];
            var prev = closes[^2];
            var a = atr.Last();
            if (last > prev + a) return "Buy";
            if (last < prev - a) return "Sell";
            return "Hold";
        }

        public static string MacdDivergence(List<decimal> closes, List<decimal> macd)
        {
            if (closes.Count < 3 || macd.Count < 3) return "Hold";
            bool priceLowerLow = closes[^1] < closes[^2] && closes[^2] <= closes[^3];
            bool macdHigherLow = macd[^1] > macd[^2] && macd[^2] <= macd[^3];
            if (priceLowerLow && macdHigherLow) return "Buy";
            bool priceHigherHigh = closes[^1] > closes[^2] && closes[^2] >= closes[^3];
            bool macdLowerHigh = macd[^1] < macd[^2] && macd[^2] >= macd[^3];
            if (priceHigherHigh && macdLowerHigh) return "Sell";
            return "Hold";
        }

        public static string Combine(params string[] signals)
        {
            var buys = signals.Count(s => s == "Buy");
            var sells = signals.Count(s => s == "Sell");
            if (buys >= 2 && buys > sells) return "Buy";
            if (sells >= 2 && sells > buys) return "Sell";
            return "Hold";
        }
    }
}
