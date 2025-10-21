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

    public record StrategySignal(string Signal, decimal Strength, string Reason);

    public static class Strategies
    {
        private static StrategySignal Hold(string reason = "no setup") =>
            new("Hold", 0m, reason);

        private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));

        // ------------------------------------------------------------
        // 1) EMA Crossover + RSI Filter (trend-following with momentum gate)
        // ------------------------------------------------------------
        public static StrategySignal EmaRsi(
            List<decimal> closes,
            List<decimal> emaShort,  // e.g., 9
            List<decimal> emaLong,   // e.g., 21
            int rsiPeriod = 14,
            (decimal min, decimal max)? longRsiBand = null,   // default (50..70)
            (decimal min, decimal max)? shortRsiBand = null   // default (25..50)
        )
        {
            if (closes.Count < 3 || emaShort.Count < 2 || emaLong.Count < 2)
                return Hold("insufficient data");

            longRsiBand ??= (50m, 70m);
            shortRsiBand ??= (25m, 50m);

            var rsi = Indicators.RSI(closes, rsiPeriod);

            bool crossUp = emaShort[^1] > emaLong[^1] && emaShort[^2] <= emaLong[^2];
            bool crossDown = emaShort[^1] < emaLong[^1] && emaShort[^2] >= emaLong[^2];

            var price = closes[^1];
            var emaGap = Math.Abs(emaShort[^1] - emaLong[^1]);
            var relGap = price > 0 ? Clamp01(emaGap / price * 5m) : 0m;   // scale

            if (crossUp && rsi > longRsiBand.Value.min && rsi < longRsiBand.Value.max)
            {
                // strength grows with EMA separation and RSI distance above 50
                var rsiBoost = Clamp01((rsi - 50m) / 25m);
                var strength = Clamp01(relGap * 0.6m + rsiBoost * 0.6m);
                return new("Buy", strength, $"EMA↑; rsi={rsi:F1}; gap={emaGap:F4}");
            }

            if (crossDown && rsi < shortRsiBand.Value.max && rsi > shortRsiBand.Value.min)
            {
                var rsiBoost = Clamp01((50m - rsi) / 25m);
                var strength = Clamp01(relGap * 0.6m + rsiBoost * 0.6m);
                return new("Sell", strength, $"EMA↓; rsi={rsi:F1}; gap={emaGap:F4}");
            }

            return Hold($"no cross / rsi={rsi:F1}");
        }

        // ------------------------------------------------------------
        // 2) Bollinger Band Mean Reversion (contrarian)
        //    Uses null-safe bands from improved indicator
        // ------------------------------------------------------------
        public static StrategySignal BollingerMeanReversion(
            List<decimal> closes,
            List<decimal?> upper,
            List<decimal?> lower,
            List<decimal?>? middle = null)
        {
            if (closes.Count == 0 || upper.Count == 0 || lower.Count == 0)
                return Hold("no data");

            var price = closes[^1];
            var ub = upper[^1];
            var lb = lower[^1];

            if (ub is null || lb is null)
                return Hold("bands not ready");

            var bandWidth = Math.Max(1e-8m, (ub.Value - lb.Value));
            if (bandWidth <= 0) return Hold("invalid bands");

            // z-score like measure relative to band edges
            if (price <= lb.Value)
            {
                var depth = Clamp01((lb.Value - price) / bandWidth * 2m);
                return new("Buy", depth, $"price<=LB; z~{-depth:F3}");
            }
            if (price >= ub.Value)
            {
                var depth = Clamp01((price - ub.Value) / bandWidth * 2m);
                return new("Sell", depth, $"price>=UB; z~{depth:F3}");
            }

            return Hold("inside bands");
        }

        // ------------------------------------------------------------
        // 3) ATR Volatility Breakout (confirmation of expansion)
        //    Buy if last > prevHigh + k*ATR ; Sell if last < prevLow - k*ATR
        // ------------------------------------------------------------
        public static StrategySignal AtrBreakout(
            List<decimal> closes,
            List<decimal> highs,
            List<decimal> lows,
            List<decimal> atr,        // Wilder ATR list (aligned)
            decimal k = 0.5m)
        {
            if (closes.Count < 2 || highs.Count < 2 || lows.Count < 2 || atr.Count < 1)
                return Hold("insufficient data");

            var last = closes[^1];
            var prevHigh = highs[^2];
            var prevLow = lows[^2];
            var a = atr[^1];
            if (a <= 0) return Hold("atr<=0");

            var buyThresh = prevHigh + k * a;
            var sellThresh = prevLow - k * a;

            if (last > buyThresh)
            {
                var strength = Clamp01((last - buyThresh) / Math.Max(a, 1e-8m));
                return new("Buy", strength, $"breakout↑ last={last:F2} > {buyThresh:F2} (ATR={a:F2})");
            }
            if (last < sellThresh)
            {
                var strength = Clamp01((sellThresh - last) / Math.Max(a, 1e-8m));
                return new("Sell", strength, $"breakdown↓ last={last:F2} < {sellThresh:F2} (ATR={a:F2})");
            }

            return Hold("no breakout");
        }

        // ------------------------------------------------------------
        // 4) MACD Divergence (simple 3-point structure + histogram tone)
        //    Bullish: price lower low while MACD higher low
        //    Bearish: price higher high while MACD lower high
        // ------------------------------------------------------------
        public static StrategySignal MacdDivergence(
            List<decimal> closes,
            List<decimal> macd,
            List<decimal> signal,
            List<decimal> hist)
        {
            if (closes.Count < 3 || macd.Count < 3 || signal.Count < 3 || hist.Count < 3)
                return Hold("insufficient data");

            bool priceLowerLow = closes[^1] < closes[^2] && closes[^2] <= closes[^3];
            bool macdHigherLow = macd[^1] > macd[^2] && macd[^2] <= macd[^3];

            bool priceHigherHigh = closes[^1] > closes[^2] && closes[^2] >= closes[^3];
            bool macdLowerHigh = macd[^1] < macd[^2] && macd[^2] >= macd[^3];

            // Use histogram slope as confirmation
            var histSlope = hist[^1] - hist[^2];

            if (priceLowerLow && macdHigherLow && histSlope > 0)
            {
                var mag = Math.Abs(macd[^1] - macd[^2]);
                var strength = Clamp01((decimal)mag * 2m);
                return new("Buy", strength, $"bull div; ΔMACD={mag:F4}; ΔHist={histSlope:F4}");
            }

            if (priceHigherHigh && macdLowerHigh && histSlope < 0)
            {
                var mag = Math.Abs(macd[^1] - macd[^2]);
                var strength = Clamp01((decimal)mag * 2m);
                return new("Sell", strength, $"bear div; ΔMACD={mag:F4}; ΔHist={histSlope:F4}");
            }

            return Hold("no divergence");
        }

        // ------------------------------------------------------------
        // 5) Weighted Combiner
        //    Votes: Buy = +strength, Sell = -strength, Hold = 0
        // ------------------------------------------------------------
        public static StrategySignal Combine(params StrategySignal[] parts)
        {
            if (parts is null || parts.Length == 0) return Hold("no parts");

            decimal score = 0m;
            foreach (var p in parts)
            {
                if (p.Signal == "Buy") score += p.Strength;
                //else if (p.Signal == "Sell") score -= p.Strength;
                else score -= p.Strength;
            }

            var absScore = Math.Abs(score);
            var signal = absScore < 0.15m ? "Hold" : (score > 0 ? "Buy" : "Sell");

            // aggregate short reason
            var reasons = string.Join(" | ", parts.Select(p => $"{p.Signal}:{p.Strength:F2}"));
            return new(signal, Clamp01(absScore), reasons);
        }
    }
}
