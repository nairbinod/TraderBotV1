namespace TraderBotV1
{
    public static class Indicators
    {
        // --- Simple Moving Average ---
        public static List<decimal> SMAList(List<decimal> vals, int period)
        {
            var output = new List<decimal>(vals.Count);
            for (int i = 0; i < vals.Count; i++)
            {
                if (i + 1 < period)
                {
                    output.Add(0);
                    continue;
                }
                output.Add(vals.Skip(i + 1 - period).Take(period).Average());
            }
            return output;
        }

        // --- Exponential Moving Average (seeded with SMA) ---
        public static List<decimal> EMAList(List<decimal> vals, int period)
        {
            var ema = new List<decimal>();
            if (vals.Count < period) return vals.ToList();

            decimal k = 2m / (period + 1);
            decimal prevEma = vals.Take(period).Average(); // seed with SMA(period)

            for (int i = 0; i < vals.Count; i++)
            {
                prevEma = i < period ? prevEma : vals[i] * k + prevEma * (1 - k);
                ema.Add(prevEma);
            }
            return ema;
        }

        // --- RSI (Wilder's smoothing) ---
        public static decimal RSI(List<decimal> closes, int period = 14)
        {
            if (closes.Count <= period) return 50m;

            decimal avgGain = 0, avgLoss = 0;
            for (int i = 1; i <= period; i++)
            {
                var diff = closes[i] - closes[i - 1];
                if (diff > 0) avgGain += diff;
                else avgLoss -= diff;
            }

            avgGain /= period;
            avgLoss /= period;

            for (int i = period + 1; i < closes.Count; i++)
            {
                var diff = closes[i] - closes[i - 1];
                avgGain = (avgGain * (period - 1) + Math.Max(diff, 0)) / period;
                avgLoss = (avgLoss * (period - 1) + Math.Max(-diff, 0)) / period;
            }

            if (avgLoss == 0) return 100m;
            var rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
        }

        // --- MACD with Signal + Histogram ---
        public static (List<decimal> macd, List<decimal> signal, List<decimal> hist) MACDSeries(
            List<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
        {
            if (closes.Count < slow)
                return (new List<decimal>(), new List<decimal>(), new List<decimal>());

            var emaFast = EMAList(closes, fast);
            var emaSlow = EMAList(closes, slow);

            var macd = new List<decimal>(closes.Count);
            for (int i = 0; i < closes.Count; i++)
                macd.Add(emaFast[i] - emaSlow[i]);

            var validMacd = macd.Skip(slow - 1).ToList();
            var sig = EMAList(validMacd, signal);
            var paddedSig = Enumerable.Repeat(0m, slow - 1).Concat(sig).ToList();

            var hist = new List<decimal>(macd.Count);
            for (int i = 0; i < macd.Count; i++)
                hist.Add(macd[i] - paddedSig[i]);

            return (macd, paddedSig, hist);
        }

        // --- Improved Bollinger Bands ---
        public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
            BollingerBands(List<decimal> closes, int period = 20, decimal mult = 2m)
        {
            var middle = SMAList(closes, period).Select(x => (decimal?)x).ToList();
            var upper = new List<decimal?>(closes.Count);
            var lower = new List<decimal?>(closes.Count);

            for (int i = 0; i < closes.Count; i++)
            {
                if (i + 1 < period)
                {
                    upper.Add(null);
                    lower.Add(null);
                    continue;
                }

                var window = closes.GetRange(i + 1 - period, period);
                var mean = middle[i] ?? window.Average();
                var variance = window.Average(v => (v - mean) * (v - mean));
                var sd = (decimal)Math.Sqrt((double)variance);

                upper.Add(mean + mult * sd);
                lower.Add(mean - mult * sd);
            }

            return (upper, middle, lower);
        }

        // --- ATR (Wilder's smoothing) ---
        public static List<decimal> ATRList(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
        {
            if (closes.Count <= period)
                return new List<decimal>();

            var trs = new List<decimal>();
            for (int i = 1; i < closes.Count; i++)
            {
                var tr = Math.Max(highs[i] - lows[i],
                    Math.Max(Math.Abs(highs[i] - closes[i - 1]),
                             Math.Abs(lows[i] - closes[i - 1])));
                trs.Add(tr);
            }

            var atr = new List<decimal>();
            decimal firstATR = trs.Take(period).Average();
            atr.Add(firstATR);

            for (int i = period; i < trs.Count; i++)
            {
                decimal currentATR = (atr.Last() * (period - 1) + trs[i]) / period;
                atr.Add(currentATR);
            }

            // Pad zeros to align list with closes length
            var padCount = closes.Count - atr.Count;
            atr.InsertRange(0, Enumerable.Repeat(0m, padCount));

            return atr;
        }
    }
}

