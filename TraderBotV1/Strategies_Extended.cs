using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderBotV1
{
	public static class StrategiesExtended
	{
		private static decimal Clamp01(decimal v) => Math.Min(1m, Math.Max(0m, v));
		private static StrategySignal Hold(string reason) => new("Hold", 0m, reason);

		// --- ADX Filter ---
		public static StrategySignal AdxFilter(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14, decimal threshold = 25m)
		{
			var (adx, diPlus, diMinus) = IndicatorsExtended.ADXList(highs, lows, closes, period);
			if (adx.Count < 3) return Hold("ADX insufficient data");


			int last = adx.Count - 1;
			decimal currentAdx = adx[last];
			decimal prevAdx = adx[Math.Max(0, last - 1)];
			decimal diP = diPlus[last];
			decimal diM = diMinus[last];


			// --- Trend filters ---
			bool strongTrend = currentAdx >= threshold && currentAdx > prevAdx; // rising ADX
			bool upDominant = diP > diM + 5m; // DI+ leads DI- by margin
			bool downDominant = diM > diP + 5m;


			// --- Volatility context: avoid noise in low movement zones ---
			decimal avg = closes.Average();
			decimal recentRange = (highs.Max() - lows.Min()) / Math.Max(avg, 1e-8m);
			if (recentRange < 0.01m)
				return Hold("Low volatility: ignore ADX noise");


			if (strongTrend && upDominant)
			{
				var strength = Clamp01((currentAdx - threshold) / 20m);
				return new("Buy", strength, $"ADX rising↑ ({currentAdx:F1}) DI+>{diP:F1}, DI-={diM:F1}");
			}


			if (strongTrend && downDominant)
			{
				var strength = Clamp01((currentAdx - threshold) / 20m);
				return new("Sell", strength, $"ADX falling↓ ({currentAdx:F1}) DI->{diM:F1}, DI+={diP:F1}");
			}


			return Hold($"ADX neutral {currentAdx:F1} (DI+={diP:F1}, DI-={diM:F1})");
		}

		// --- Volume Confirmation ---
		public static StrategySignal VolumeConfirm(List<decimal> closes, List<decimal> volumes, int period = 20, decimal spikeMultiple = 1.5m)
		{
			if (volumes == null || volumes.Count < closes.Count || volumes.Count < period)
				return Hold("Volume data insufficient");

			var volSma = IndicatorsExtended.EMAList(volumes, period);
			int i = closes.Count - 1;
			if (i < 1) return Hold("not enough bars");

			decimal v = volumes[i];
			decimal baseV = volSma[i];
			if (baseV <= 0) return Hold("Invalid volume baseline");

			decimal mult = v / baseV;
			bool upBar = closes[i] > closes[i - 1];

			if (mult >= spikeMultiple)
			{
				var strength = Clamp01((mult - spikeMultiple) / spikeMultiple);
				return new(upBar ? "Buy" : "Sell", strength, $"Volume spike x{mult:F2} {(upBar ? "up" : "down")} bar");
			}

			return Hold("No volume confirmation");
		}

		// --- CCI Reversion ---
		public static StrategySignal CciReversion(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
		{
			var cci = IndicatorsExtended.CCIList(highs, lows, closes, period);
			if (cci.Count < 3) return Hold("CCI insufficient data");

			decimal now = cci.Last();
			decimal prev = cci[cci.Count - 2];

			bool crossUp = prev <= -100m && now > -100m;
			bool crossDn = prev >= 100m && now < 100m;

			if (crossUp)
			{
				var strength = Clamp01((Math.Abs(prev) - 100m) / 150m);
				return new("Buy", strength, $"CCI cross↑ ({prev:F0}->{now:F0})");
			}
			if (crossDn)
			{
				var strength = Clamp01((Math.Abs(prev) - 100m) / 150m);
				return new("Sell", strength, $"CCI cross↓ ({prev:F0}->{now:F0})");
			}

			return Hold($"CCI={now:F0}");
		}

		// --- Donchian Breakout ---
		public static StrategySignal DonchianBreakout(
	List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 20)
		{
			if (highs == null || lows == null || closes == null || closes.Count < period + 5)
				return Hold("Donchian insufficient data");

			var (upper, lower) = IndicatorsExtended.DonchianChannel(highs, lows, period);
			if (upper.Count < closes.Count || lower.Count < closes.Count)
				return Hold("Donchian incomplete");

			var atr = IndicatorsExtended.ATRList(highs, lows, closes, 14);
			var rsi = Indicators.RSIList(closes, 14);
			var ema20 = Indicators.EMAList(closes, 20);
			var (adx, diPlus, diMinus) = IndicatorsExtended.ADXList(highs, lows, closes, 14);

			int last = closes.Count - 1;
			decimal price = closes[last];
			decimal u = upper[last];
			decimal l = lower[last];
			decimal vol = (atr.Count > last && closes[last] > 0) ? atr[last] / closes[last] : 0m;
			decimal rsiNow = (rsi.Count > last) ? rsi[last] : 50m;
			decimal emaSlope = ema20[last] - ema20[Math.Max(0, last - 2)];
			decimal adxNow = (adx.Count > last) ? adx[last] : 0m;

			// --- Adaptive thresholds ---
			bool strongVol = vol > 0.006m; // at least 0.6% ATR volatility
			bool strongTrend = adxNow > 25m; // trending market
			bool bullishSlope = emaSlope > 0; // uptrend bias
			bool healthyRsi = rsiNow > 45m && rsiNow < 65m; // momentum in breakout zone

			// --- Retest confirmation ---
			bool breakoutValidated = price > u && closes[last - 1] <= highs[last - 1];

			// --- BUY CONDITION ---
			if (breakoutValidated && strongVol && strongTrend && bullishSlope && healthyRsi)
			{
				var strength = Clamp01((price - u) / Math.Max(price * 0.02m, 1e-8m));
				return new("Buy", strength,
					$"Donchian breakout ↑ confirmed (RSI={rsiNow:F1}, ADX={adxNow:F1}, ATR%={vol:P2})");
			}

			// --- SELL CONDITION ---
			bool bearishSlope = emaSlope < 0;
			bool breakdownValidated = price < l && closes[last - 1] >= lows[last - 1];

			if (breakdownValidated && strongVol && strongTrend && bearishSlope && healthyRsi)
			{
				var strength = Clamp01((l - price) / Math.Max(price * 0.02m, 1e-8m));
				return new("Sell", strength,
					$"Donchian breakdown ↓ confirmed (RSI={rsiNow:F1}, ADX={adxNow:F1}, ATR%={vol:P2})");
			}

			return Hold($"No confirmed breakout (RSI={rsiNow:F1}, ADX={adxNow:F1}, ATR%={vol:P2})");
		}


		// --- Pivot Reversal ---
		public static StrategySignal PivotReversal(List<decimal> highs, List<decimal> lows, List<decimal> closes)
		{
			if (closes.Count < 3) return Hold("Not enough bars");

			decimal prevHigh = highs[^2];
			decimal prevLow = lows[^2];
			decimal prevClose = closes[^2];
			var (P, R1, S1) = IndicatorsExtended.PivotPoints(prevHigh, prevLow, prevClose);

			decimal last = closes.Last();

			if (last >= R1 && last > prevClose)
			{
				var diff = last - R1;
				var strength = Clamp01(diff / Math.Max(last * 0.01m, 1e-8m));
				return new("Sell", strength, $"Near/Above R1 {R1:F2}");
			}
			if (last <= S1 && last < prevClose)
			{
				var diff = S1 - last;
				var strength = Clamp01(diff / Math.Max(last * 0.01m, 1e-8m));
				return new("Buy", strength, $"Near/Below S1 {S1:F2}");
			}

			return Hold($"Pivot P={P:F2}");
		}

		// --- StochRSI Reversal ---
		public static StrategySignal StochRsiReversal(List<decimal> closes, int rsiPeriod = 14, int stochPeriod = 14, int smoothK = 3, int smoothD = 3)
		{
			var (k, d) = IndicatorsExtended.StochRSIList(closes, rsiPeriod, stochPeriod, smoothK, smoothD);
			if (k.Count < 3 || d.Count < 3) return Hold("StochRSI insufficient data");

			decimal kNow = Clamp01(k.Last());
			decimal kPrev = Clamp01(k[k.Count - 2]);
			decimal dNow = Clamp01(d.Last());

			if (kNow > 0.4m && kNow < 0.6m && dNow > 0.4m && dNow < 0.6m)
				return Hold("StochRSI neutral");

			bool crossUp = kPrev <= 0.2m && kNow > 0.2m && kNow > dNow;
			bool crossDn = kPrev >= 0.8m && kNow < 0.8m && kNow < dNow;

			if (crossUp)
			{
				var strength = Clamp01((decimal)Math.Sqrt((double)((kNow - 0.2m) / 0.3m)));
				return new("Buy", strength, $"StochRSI cross↑ (k={kNow:F2}, d={dNow:F2})");
			}
			if (crossDn)
			{
				var strength = Clamp01((decimal)Math.Sqrt((double)((0.8m - kNow) / 0.3m)));
				return new("Sell", strength, $"StochRSI cross↓ (k={kNow:F2}, d={dNow:F2})");
			}

			return Hold($"StochRSI stable k={kNow:F2}, d={dNow:F2}");
		}

		// --- EMA 200 Regime Filter ---
		public static StrategySignal Ema200RegimeFilter(List<decimal> closes, int period = 200)
		{
			var ema = Indicators.EMAList(closes, period);
			if (ema.Count < 2) return Hold("EMA data insufficient");

			decimal price = closes.Last();
			decimal emaVal = ema.Last();

			if (price > emaVal)
			{
				var strength = Clamp01((price - emaVal) / Math.Max(price * 0.02m, 1e-8m));
				return new("Buy", strength, $"Above EMA{period} {emaVal:F2}");
			}
			if (price < emaVal)
			{
				var strength = Clamp01((emaVal - price) / Math.Max(price * 0.02m, 1e-8m));
				return new("Sell", strength, $"Below EMA{period} {emaVal:F2}");
			}

			return Hold($"EMA{period} neutral");
		}
	}
}
