
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

        // ─────────────────────────────────────────────────────────────────────────────
        // 1) EMA Crossover + RSI Filter (trend-following with momentum gate)
        // ─────────────────────────────────────────────────────────────────────────────
        public static StrategySignal EmaRsi(
    List<decimal> closes,
    List<decimal> emaShort,  // e.g., 9-period EMA
    List<decimal> emaLong,   // e.g., 21-period EMA
    int rsiPeriod = 14,
    (decimal min, decimal max)? longRsiBand = null,   // default (55..70)
    (decimal min, decimal max)? shortRsiBand = null   // default (30..45)
)
        {
            if (closes.Count < 3 || emaShort.Count < 3 || emaLong.Count < 3)
                return Hold("insufficient data");

            longRsiBand ??= (55m, 70m);   // stricter for Buy
            shortRsiBand ??= (30m, 45m);  // stricter for Sell

            var rsiNow = Indicators.RSI(closes, rsiPeriod);
            var rsiPrev = Indicators.RSI(closes.Take(closes.Count - 1).ToList(), rsiPeriod);

            bool crossUp = emaShort[^1] > emaLong[^1] && emaShort[^2] <= emaLong[^2];
            bool crossDown = emaShort[^1] < emaLong[^1] && emaShort[^2] >= emaLong[^2];

            // ➕ Strong slope confirmation (last 3 bars)
            bool slopeUp = emaShort[^1] > emaShort[^2] && emaShort[^2] > emaShort[^3];
            bool slopeDown = emaShort[^1] < emaShort[^2] && emaShort[^2] < emaShort[^3];

            // ➕ RSI cross of 50 (momentum shift)
            bool rsiCrossedUp = rsiPrev <= 50m && rsiNow > 50m;
            bool rsiCrossedDown = rsiPrev >= 50m && rsiNow < 50m;

            var price = closes[^1];
            var emaGap = Math.Abs(emaShort[^1] - emaLong[^1]);
            var gapRel = price > 0 ? emaGap / price : 0m;

            // ➕ Minimum EMA gap filter (avoid micro crosses)
            bool strongGap = gapRel > 0.002m; // 0.2%

            if (crossUp && slopeUp && strongGap && rsiCrossedUp &&
                rsiNow > longRsiBand.Value.min && rsiNow < longRsiBand.Value.max)
            {
                var strength = Clamp01(gapRel * 8m * 0.5m + Clamp01((rsiNow - 55m) / 20m) * 0.5m);
                return new("Buy", strength, $"EMA↑ strong + RSI↑; rsi={rsiNow:F1}; gap={emaGap:F4}");
            }

            if (crossDown && slopeDown && strongGap && rsiCrossedDown &&
                rsiNow < shortRsiBand.Value.max && rsiNow > shortRsiBand.Value.min)
            {
                var strength = Clamp01(gapRel * 8m * 0.5m + Clamp01((45m - rsiNow) / 20m) * 0.5m);
                return new("Sell", strength, $"EMA↓ strong + RSI↓; rsi={rsiNow:F1}; gap={emaGap:F4}");
            }

            return Hold($"no cross/confirm; rsi={rsiNow:F1}");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 2) Bollinger Band Mean Reversion (contrarian with trend+exhaustion filters)
        // ─────────────────────────────────────────────────────────────────────────────

        public static StrategySignal BollingerMeanReversion(
            List<decimal> closes,
            List<decimal?> upper,
            List<decimal?> lower,
            List<decimal?>? middle = null,
            int rsiPeriod = 14)
        {
            if (closes.Count == 0 || upper.Count == 0 || lower.Count == 0)
                return Hold("no data");

            var price = closes[^1];
            var ub = upper[^1];
            var lb = lower[^1];

            if (ub is null || lb is null) return Hold("bands not ready");

            var bandWidth = Math.Max(1e-8m, (ub.Value - lb.Value));
            if (bandWidth <= 0) return Hold("invalid bands");

            // Ignore ultra-narrow bands (<0.5%)
            if (bandWidth / Math.Max(1e-8m, price) < 0.005m)
                return Hold("bands too narrow");

            // Trend context via middle-band slope (avoid counter-trend knives)
            decimal midSlope = 0m;
            bool strongDowntrend = false;
            bool strongUptrend = false;
            if (middle is not null && middle.Count >= 3 &&
                middle[^1].HasValue && middle[^2].HasValue && middle[^3].HasValue)
            {
                midSlope = middle[^1]!.Value - middle[^3]!.Value;
                strongDowntrend = middle[^1]!.Value < middle[^2]!.Value && middle[^2]!.Value < middle[^3]!.Value;
                strongUptrend = middle[^1]!.Value > middle[^2]!.Value && middle[^2]!.Value > middle[^3]!.Value;
            }

            // RSI exhaustion
            var rsi = Indicators.RSI(closes, rsiPeriod);

            // Optional reversal candle check
            bool reversalUp = closes[^1] > closes[^2];
            bool reversalDown = closes[^1] < closes[^2];

            // BUY condition
            if (price <= lb.Value)
            {
                if (strongDowntrend) return Hold("trend too bearish");
                if (midSlope < -price * 0.002m) return Hold("downtrend too strong");
                if (rsi >= 25m) return Hold("RSI not extreme");
                if (!reversalUp) return Hold("no reversal candle");

                var depth = Clamp01((lb.Value - price) / bandWidth * 2m);
                var strength = Clamp01(depth * 0.6m + Clamp01((25m - rsi) / 20m) * 0.4m);
                return new("Buy", strength, $"LB touch + RSI<25 + reversal; midSlope={midSlope:F4}");
            }

            // SELL condition
            if (price >= ub.Value)
            {
                if (strongUptrend) return Hold("trend too bullish");
                if (midSlope > price * 0.002m) return Hold("uptrend too strong");
                if (rsi <= 75m) return Hold("RSI not extreme");
                if (!reversalDown) return Hold("no reversal candle");

                var depth = Clamp01((price - ub.Value) / bandWidth * 2m);
                var strength = Clamp01(depth * 0.6m + Clamp01((rsi - 75m) / 20m) * 0.4m);
                return new("Sell", strength, $"UB touch + RSI>75 + reversal; midSlope={midSlope:F4}");
            }

            return Hold("inside bands");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 3) ATR Volatility Breakout (requires expansion + momentum confirmation)
        // ─────────────────────────────────────────────────────────────────────────────
        public static StrategySignal AtrBreakout(
            List<decimal> closes,
            List<decimal> highs,
            List<decimal> lows,
            List<decimal> atr,        // Wilder ATR list (aligned)
            decimal k = 1.0m, // k = 0.5m, 
            int rsiPeriod = 14,
            int emaFast = 9,
            int emaSlow = 21
        )
        {


            if (closes.Count < emaSlow + 1 || atr.Count < emaSlow + 1)
                return Hold("insufficient data");

            var last = closes[^1];
            var prevHigh = highs[^2];
            var prevLow = lows[^2];
            var a = atr[^1];
            var a0 = atr[^2];

            if (a <= 0) return Hold("atr<=0");

            // Compute EMA trend filter
            var emaFastVal = Indicators.EMA(closes, emaFast);
            var emaSlowVal = Indicators.EMA(closes, emaSlow);

            // Compute RSI
            var rsi = Indicators.RSI(closes, rsiPeriod);

            // ATR expansion filter
            var atrMedian50 = Median(atr.Skip(Math.Max(0, atr.Count - 50)).ToList());
            bool atrExpanding = a > a0 && a > atrMedian50;

            // Thresholds
            var buyThresh = prevHigh + k * a;
            var sellThresh = prevLow - k * a;

            // BUY conditions
            if (last > buyThresh && atrExpanding && rsi > 60m && emaFastVal > emaSlowVal)
            {
                var strength = Clamp01((last - buyThresh) / Math.Max(a, 1e-8m)) * 0.6m
                             + Clamp01((rsi - 60m) / 20m) * 0.4m;
                return new("Buy", Clamp01(strength), $"BO↑ last>{buyThresh:F2}; ATR↑; RSI={rsi:F1}; EMA trend");
            }

            // SELL conditions
            if (last < sellThresh && atrExpanding && rsi < 40m && emaFastVal < emaSlowVal)
            {
                var strength = Clamp01((sellThresh - last) / Math.Max(a, 1e-8m)) * 0.6m
                             + Clamp01((40m - rsi) / 20m) * 0.4m;
                return new("Sell", Clamp01(strength), $"BD↓ last<{sellThresh:F2}; ATR↑; RSI={rsi:F1}; EMA trend");
            }

            return Hold("no breakout/confirm");

        }

        private static decimal Median(IEnumerable<decimal> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var sorted = source.Where(x => x > 0).OrderBy(x => x).ToList(); // filter zeros if needed
            int count = sorted.Count;
            if (count == 0) return 0m; // avoid exception

            if (count % 2 == 0)
                return (sorted[count / 2 - 1] + sorted[count / 2]) / 2m;
            else
                return sorted[count / 2];
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 4) MACD Divergence (5-bar swing points + histogram + long-trend filter)
        // ─────────────────────────────────────────────────────────────────────────────
        public static StrategySignal MacdDivergence(
                List<decimal> closes,
                List<decimal> macd,
                List<decimal> signal,
                List<decimal> hist)
        {
            if (closes.Count < 10 || macd.Count < 10 || signal.Count < 10 || hist.Count < 10)
                return Hold("insufficient data");

            int N = 7;
            int start = Math.Max(0, closes.Count - N);

            var spanClose = closes.GetRange(start, closes.Count - start);
            var spanMacd = macd.GetRange(start, macd.Count - start);
            var spanHist = hist.GetRange(start, hist.Count - start);

            int idxCloseMin = IndexOfMin(spanClose) + start;
            int idxCloseMax = IndexOfMax(spanClose) + start;
            int idxMacdMin = IndexOfMin(spanMacd) + start;
            int idxMacdMax = IndexOfMax(spanMacd) + start;

            var ema50 = Indicators.EMAList(closes, 50);
            var ema200 = Indicators.EMAList(closes, 200);
            bool longUp = ema50[^1] > ema200[^1];
            bool longDown = ema50[^1] < ema200[^1];
            bool strongTrendUp = longUp && (ema50[^1] - ema200[^1]) / closes[^1] > 0.005m;
            bool strongTrendDown = longDown && (ema200[^1] - ema50[^1]) / closes[^1] > 0.005m;

            bool histUpTone = spanHist.TakeLast(3).All(h => h > 0);
            bool histDownTone = spanHist.TakeLast(3).All(h => h < 0);

            decimal mag = Math.Abs(macd[^1] - macd[Math.Max(0, macd.Count - 2)]);
            if (mag < 0.05m) return Hold("MACD change too small");

            bool priceLL = closes[^1] < closes[idxCloseMin];
            bool macdHL = macd[^1] > macd[idxMacdMin];

            if (priceLL && macdHL && histUpTone && strongTrendUp)
            {
                var strength = Clamp01(mag * 2m);
                return new("Buy", strength, $"bull div + hist↑ + strong trend↑; ΔMACD={mag:F4}");
            }

            bool priceHH = closes[^1] > closes[idxCloseMax];
            bool macdLH = macd[^1] < macd[idxMacdMax];

            if (priceHH && macdLH && histDownTone && strongTrendDown)
            {
                var strength = Clamp01(mag * 2m);
                return new("Sell", strength, $"bear div + hist↓ + strong trend↓; ΔMACD={mag:F4}");
            }

            return Hold("no divergence/confirm");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────────
        private static int IndexOfMin(List<decimal> list)
        {
            int idx = 0;
            for (int i = 1; i < list.Count; i++)
                if (list[i] < list[idx]) idx = i;
            return idx;
        }

        private static int IndexOfMax(List<decimal> list)
        {
            int idx = 0;
            for (int i = 1; i < list.Count; i++)
                if (list[i] > list[idx]) idx = i;
            return idx;
        }
    }
}
