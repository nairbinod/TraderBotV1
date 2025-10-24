namespace TraderBotV1
{
    public static class IndicatorsExtended
    {
        // -----------------------------
        // ADX (Average Directional Index)
        // period typically 14
        // Returns ADX list aligned to inputs (zeros for warmup)
        // -----------------------------
        public static List<decimal> ADXList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
        {
            int n = closes.Count;
            var adx = new List<decimal>(Enumerable.Repeat(0m, n));
            if (n < period + 2) return adx;

            // True Range and Directional Movements
            var tr = new decimal[n];
            var dmPlus = new decimal[n];
            var dmMinus = new decimal[n];

            for (int i = 1; i < n; i++)
            {
                var upMove = highs[i] - highs[i - 1];
                var downMove = lows[i - 1] - lows[i];

                dmPlus[i] = (upMove > downMove && upMove > 0) ? upMove : 0m;
                dmMinus[i] = (downMove > upMove && downMove > 0) ? downMove : 0m;

                var highDiff = Math.Abs(highs[i] - closes[i - 1]);
                var lowDiff = Math.Abs(lows[i] - closes[i - 1]);
                var range1 = highs[i] - lows[i];
                tr[i] = Math.Max(range1, Math.Max(highDiff, lowDiff));
            }

            // Wilder's smoothing of TR, +DM, -DM
            decimal atr = tr.Skip(1).Take(period).Sum();
            decimal smPlus = dmPlus.Skip(1).Take(period).Sum();
            decimal smMinus = dmMinus.Skip(1).Take(period).Sum();

            // DI and DX arrays (keep as decimal)
            var diPlus = new decimal[n];
            var diMinus = new decimal[n];
            var dx = new decimal[n];

            int start = period + 1; // first index where we have initial smoothed values

            diPlus[start] = (smPlus == 0 || atr == 0) ? 0 : (100m * (smPlus / atr));
            diMinus[start] = (smMinus == 0 || atr == 0) ? 0 : (100m * (smMinus / atr));
            dx[start] = (diPlus[start] + diMinus[start] == 0) ? 0 : (100m * Math.Abs((diPlus[start] - diMinus[start]) / (diPlus[start] + diMinus[start])));

            // progress smoothing forward
            for (int i = start + 1; i < n; i++)
            {
                atr = atr - (atr / period) + tr[i];
                smPlus = smPlus - (smPlus / period) + dmPlus[i];
                smMinus = smMinus - (smMinus / period) + dmMinus[i];

                diPlus[i] = (smPlus == 0 || atr == 0) ? 0 : (100m * (smPlus / atr));
                diMinus[i] = (smMinus == 0 || atr == 0) ? 0 : (100m * (smMinus / atr));

                var sum = diPlus[i] + diMinus[i];
                dx[i] = sum == 0 ? 0 : (100m * Math.Abs((diPlus[i] - diMinus[i]) / sum));
            }

            // ADX is Wilder-smooth of DX
            decimal firstAdx = dx.Skip(start).Take(period).Average();
            adx[start + period - 1] = firstAdx;

            for (int i = start + period; i < n; i++)
            {
                var prev = adx[i - 1];
                adx[i] = (prev * (period - 1) + dx[i]) / period;
            }

            return adx;
        }

        // -----------------------------
        // CCI (Commodity Channel Index)
        // period typically 20
        // -----------------------------
        public static List<decimal> CCIList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
        {
            int n = closes.Count;
            var cci = new List<decimal>(Enumerable.Repeat(0m, n));
            if (n < period) return cci;

            var tp = new List<decimal>(n);
            for (int i = 0; i < n; i++)
                tp.Add((highs[i] + lows[i] + closes[i]) / 3m);

            for (int i = period - 1; i < n; i++)
            {
                var window = tp.GetRange(i + 1 - period, period);
                var mean = window.Average();
                var meanDev = window.Average(v => Math.Abs(v - mean));
                var denom = 0.015m * Math.Max(meanDev, 1e-8m);
                cci[i] = (tp[i] - mean) / denom;
            }

            return cci;
        }

        // -----------------------------
        // Stochastic RSI (returns %K and %D lists scaled 0..1)
        // rsiPeriod=14, stochPeriod=14, smoothK=3, smoothD=3 are common
        // -----------------------------
        public static (List<decimal> k, List<decimal> d) StochRSIList(
            List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
        {
            int n = closes.Count;
            var kList = new List<decimal>(Enumerable.Repeat(0m, n));
            var dList = new List<decimal>(Enumerable.Repeat(0m, n));
            if (n < rsiPeriod + stochPeriod + Math.Max(smoothK, smoothD)) return (kList, dList);

            // Build RSI series (scalar RSI we have; create quick series)
            var rsiSeries = new List<decimal?>(Enumerable.Repeat<decimal?>(null, n));
            if (n > rsiPeriod)
            {
                decimal avgGain = 0, avgLoss = 0;
                for (int i = 1; i <= rsiPeriod; i++)
                {
                    var diff = closes[i] - closes[i - 1];
                    if (diff > 0) avgGain += diff; else avgLoss -= diff;
                }
                avgGain /= rsiPeriod; avgLoss /= rsiPeriod;

                rsiSeries[rsiPeriod] = avgLoss == 0 ? 100m : 100m - (100m / (1m + (avgGain / avgLoss)));

                for (int i = rsiPeriod + 1; i < n; i++)
                {
                    var diff = closes[i] - closes[i - 1];
                    avgGain = (avgGain * (rsiPeriod - 1) + Math.Max(diff, 0)) / rsiPeriod;
                    avgLoss = (avgLoss * (rsiPeriod - 1) + Math.Max(-diff, 0)) / rsiPeriod;
                    var rsi = avgLoss == 0 ? 100m : 100m - (100m / (1m + (avgGain / avgLoss)));
                    rsiSeries[i] = rsi;
                }
            }

            // Stoch of RSI
            var rsiVals = rsiSeries.Select(x => x ?? 50m).ToList();
            var kRaw = new List<decimal>(Enumerable.Repeat(0m, n));

            for (int i = rsiPeriod + stochPeriod; i < n; i++)
            {
                var window = rsiVals.GetRange(i + 1 - stochPeriod, stochPeriod);
                var minR = window.Min();
                var maxR = window.Max();
                var denom = Math.Max(maxR - minR, 1e-8m);
                kRaw[i] = (rsiVals[i] - minR) / denom; // 0..1
            }

            // Smooth %K and produce %D
            for (int i = 0; i < n; i++)
            {
                if (i + 1 < smoothK) { kList[i] = 0m; continue; }
                var w = kRaw.Skip(i + 1 - smoothK).Take(smoothK).Average();
                kList[i] = w;
            }
            for (int i = 0; i < n; i++)
            {
                if (i + 1 < smoothD) { dList[i] = 0m; continue; }
                var w = kList.Skip(i + 1 - smoothD).Take(smoothD).Average();
                dList[i] = w;
            }

            return (kList, dList);
        }

        // -----------------------------
        // Donchian Channels (Upper/Lower)
        // period typically 20
        // -----------------------------
        public static (List<decimal> upper, List<decimal> lower) DonchianChannel(List<decimal> highs, List<decimal> lows, int period = 20)
        {
            int n = highs.Count;
            var up = new List<decimal>(Enumerable.Repeat(0m, n));
            var lo = new List<decimal>(Enumerable.Repeat(0m, n));
            if (n < period) return (up, lo);

            for (int i = period - 1; i < n; i++)
            {
                up[i] = highs.Skip(i + 1 - period).Take(period).Max();
                lo[i] = lows.Skip(i + 1 - period).Take(period).Min();
            }
            return (up, lo);
        }

        // -----------------------------
        // Pivot Points for a single bar (classic P, R1, S1)
        // -----------------------------
        public static (decimal P, decimal R1, decimal S1) PivotPoints(decimal prevHigh, decimal prevLow, decimal prevClose)
        {
            var P = (prevHigh + prevLow + prevClose) / 3m;
            var R1 = 2m * P - prevLow;
            var S1 = 2m * P - prevHigh;
            return (P, R1, S1);
        }

        // -----------------------------
        // Volume SMA
        // -----------------------------
        public static List<decimal> VolumeSMA(List<decimal> volumes, int period = 20)
        {
            int n = volumes.Count;
            var sma = new List<decimal>(Enumerable.Repeat(0m, n));
            if (n < period) return sma;

            decimal sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += volumes[i];
                if (i >= period) sum -= volumes[i - period];
                if (i + 1 >= period) sma[i] = sum / period;
            }
            return sma;
        }

        // -----------------------------
        // EMA200 Regime helper
        // Returns EMA(period) as a list (use last value to compare with Close)
        // -----------------------------
        public static List<decimal> EMARegime(List<decimal> vals, int period = 200)
        {
            var ema = new List<decimal>(vals.Count);
            if (vals.Count < period)
            {
                // seed with simple average of whatever is available
                if (vals.Count == 0) return ema;
                decimal seed = vals.Average();
                for (int i = 0; i < vals.Count; i++) ema.Add(seed);
                return ema;
            }

            decimal k = 2m / (period + 1);
            decimal prevEma = vals.Take(period).Average();
            for (int i = 0; i < vals.Count; i++)
            {
                prevEma = i < period ? prevEma : vals[i] * k + prevEma * (1 - k);
                ema.Add(prevEma);
            }
            return ema;
        }
    }
}
