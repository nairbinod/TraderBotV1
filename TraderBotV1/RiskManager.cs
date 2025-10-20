using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraderBotV1
{
	public static class RiskManager
	{
		public static long CalculateQty(decimal equity, decimal atr, decimal riskPercent)
		{
			if (atr <= 0 || equity <= 0 || riskPercent <= 0) return 0;
			var riskAmount = equity * riskPercent;
			var qty = (long)Math.Floor((double)(riskAmount / atr));
			return Math.Max(0, qty);
		}
	}
}
