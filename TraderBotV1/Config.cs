using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraderBotV1
{
	public class Config
	{
		public string Mode { get; set; } = "Auto";           // Backtest | Live | Auto
		public bool UsePaperWhenLive { get; set; } = true;
		public string DataSource { get; set; } = "Alpaca";      // Polygon | Alpaca | Auto
		public string[] Symbols { get; set; } = new[] { "OXY" };
		public int DaysHistory { get; set; } = 5;
		public decimal RiskPercent { get; set; } = 0.01m;
		public PolygonCfg Polygon { get; set; } = new();
		public AlpacaCfg Alpaca { get; set; } = new();
	}

	public class PolygonCfg
	{
		public string ApiKey { get; set; } = "YOUR_POLYGON_KEY";
		public string Timespan { get; set; } = "day"; // minute | hour | day
	}

	public class AlpacaCfg
	{
		public string ApiKey { get; set; } = "PK6GWM2KES7G5KLGGE6JU7YLBM";
		public string ApiSecret { get; set; } = "52KynJynvxKWra3j1zM4UJDxdyMELbCVS18UVHfWYXfS";
	}

}
