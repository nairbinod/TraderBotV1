namespace TraderBotV1
{
    public static class StrategiesExtended
    {
        private static StrategySignal Hold(string reason = "no setup") =>
            new("Hold", 0m, reason);

        private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));

        // 1) ADX Trend Strength Filter
        public static StrategySignal AdxFilter(
            List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14, decimal threshold = 25m)
        {
            var adx = IndicatorsExtended.ADXList(highs, lows, closes, period);
            if (adx.Count == 0) return Hold("adx no data");

            var a = adx[^1];
            if (a >= threshold)
            {
                // Strength scales with how far ADX is above threshold
                var strength = Clamp01((a - threshold) / 20m);
                return new("Buy", strength, $"ADX strong={a:F1}");
            }
            else if (a <= threshold - 5m)
            {
                var strength = Clamp01((threshold - 5m - a) / 20m);
                return new("Sell", strength, $"ADX weak={a:F1}");
            }
            return Hold($"ADX neutral={a:F1}");
        }

        // 2) Volume Confirmation Strategy (spike vs SMA)
        public static StrategySignal VolumeConfirm(
            List<decimal> closes, List<decimal> volumes, int period = 20, decimal spikeMultiple = 1.5m)
        {
            if (volumes == null || volumes.Count != closes.Count || volumes.Count < period)
                return Hold("no volumes");

            var volSma = IndicatorsExtended.VolumeSMA(volumes, period);
            var v = volumes[^1];
            var baseV = volSma[^1];
            if (baseV <= 0) return Hold("vol sma 0");

            var mult = v / baseV;
            if (mult >= spikeMultiple)
            {
                // If price closed up, treat as Buy-confirmation; if down, Sell-confirmation
                bool up = closes[^1] >= closes[^2];
                var strength = Clamp01((mult - spikeMultiple) / spikeMultiple);
                return new(up ? "Buy" : "Sell", strength, $"Vol spike x{mult:F2} ({(up ? "up" : "down")} bar)");
            }

            return Hold("no volume spike");
        }

        // 3) CCI Mean Reversion
        public static StrategySignal CciReversion(
            List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
        {
            var cci = IndicatorsExtended.CCIList(highs, lows, closes, period);
            if (cci.Count == 0) return Hold("cci no data");

            var now = cci[^1];
            var prev = cci[Math.Max(0, cci.Count - 2)];

            bool crossUp = prev <= -100m && now > -100m;
            bool crossDn = prev >= 100m && now < 100m;

            if (crossUp)
            {
                var strength = Clamp01((-100m - prev) / 100m);
                return new("Buy", strength, $"CCI cross -100 up ({prev:F0}->{now:F0})");
            }
            if (crossDn)
            {
                var strength = Clamp01((prev - 100m) / 100m);
                return new("Sell", strength, $"CCI cross +100 down ({prev:F0}->{now:F0})");
            }

            return Hold($"CCI={now:F0}");
        }

        // 4) Donchian Channel Breakout
        public static StrategySignal DonchianBreakout(
            List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
        {
            var (up, lo) = IndicatorsExtended.DonchianChannel(highs, lows, period);
            if (up.Count == 0) return Hold("donchian no data");

            var last = closes[^1];
            var u = up[^1];
            var l = lo[^1];

            if (last > u)
            {
                var strength = Clamp01((last - u) / Math.Max(last * 0.01m, 1e-8m));
                return new("Buy", strength, $"Donchian BO↑ last>{u:F2}");
            }
            if (last < l)
            {
                var strength = Clamp01((l - last) / Math.Max(last * 0.01m, 1e-8m));
                return new("Sell", strength, $"Donchian BD↓ last<{l:F2}");
            }

            return Hold("inside channel");
        }

        // 5) Pivot Point Reversal (R1/S1)
        public static StrategySignal PivotReversal(List<decimal> highs, List<decimal> lows, List<decimal> closes)
        {
            if (closes.Count < 2) return Hold("not enough bars");
            var prevHigh = highs[^2];
            var prevLow = lows[^2];
            var prevClose = closes[^2];

            var (P, R1, S1) = IndicatorsExtended.PivotPoints(prevHigh, prevLow, prevClose);
            var last = closes[^1];

            if (last >= R1 && last > prevClose)
            {
                var diff = last - R1;
                var strength = Clamp01(diff / Math.Max(last * 0.01m, 1e-8m));
                return new("Sell", strength, $"Near/Above R1 ({R1:F2})");
            }
            if (last <= S1 && last < prevClose)
            {
                var diff = S1 - last;
                var strength = Clamp01(diff / Math.Max(last * 0.01m, 1e-8m));
                return new("Buy", strength, $"Near/Below S1 ({S1:F2})");
            }

            return Hold($"P={P:F2}");
        }

        // 6) Stochastic RSI Reversal
        public static StrategySignal StochRsiReversal(List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
        {
            var (k, d) = IndicatorsExtended.StochRSIList(closes, rsiPeriod, stochPeriod, smoothK, smoothD);
            if (k.Count == 0) return Hold("stochrsi no data");

            var kNow = k[^1];
            var kPrev = k[Math.Max(0, k.Count - 2)];
            var dNow = d[^1];

            bool crossUp = kPrev <= 0.2m && kNow > 0.2m && kNow > dNow;
            bool crossDn = kPrev >= 0.8m && kNow < 0.8m && kNow < dNow;

            if (crossUp)
            {
                var strength = Clamp01((kNow - 0.2m) / 0.3m);
                return new("Buy", strength, $"StochRSI cross↑ 0.2 (k={kNow:F2}, d={dNow:F2})");
            }
            if (crossDn)
            {
                var strength = Clamp01((0.8m - kNow) / 0.3m);
                return new("Sell", strength, $"StochRSI cross↓ 0.8 (k={kNow:F2}, d={dNow:F2})");
            }

            return Hold($"StochRSI k={kNow:F2}, d={dNow:F2}");
        }

        // 7) EMA200 Regime Filter (trend vs range)
        public static StrategySignal Ema200RegimeFilter(List<decimal> closes, int period = 200)
        {
            var ema = IndicatorsExtended.EMARegime(closes, period);
            if (ema.Count == 0) return Hold("ema200 no data");

            var last = closes[^1];
            var e = ema[^1];

            if (last > e)
            {
                var strength = Clamp01((last - e) / Math.Max(last * 0.02m, 1e-8m));
                return new("Buy", strength, $"Regime: Up (C>{period}EMA)");
            }
            if (last < e)
            {
                var strength = Clamp01((e - last) / Math.Max(last * 0.02m, 1e-8m));
                return new("Sell", strength, $"Regime: Down (C<{period}EMA)");
            }

            return Hold("Regime: Flat");
        }
    }
}
