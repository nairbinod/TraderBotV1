using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraderBotV1
{
	public static class TradeLogger
	{
		private static readonly string DbFile = Path.Combine(Directory.GetCurrentDirectory(), "SmartBot.db");
		private static readonly string ConnStr = $"Data Source={DbFile};Version=3;";

		public static void InitializeDatabase()
		{
			if (!File.Exists(DbFile))
			{
				SqliteConnection.CreateFile(DbFile);
			}

			using var c = new SQLiteConnection(ConnStr);
			c.Open();
			var create = @"
            CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ts TEXT,
                symbol TEXT,
                action TEXT,
                price REAL,
                qty INTEGER,
                rsi REAL,
                macd REAL,
                macd_signal REAL,
                atr REAL,
                sma REAL,
                ub REAL,
                lb REAL,
                equity REAL
            );";
			using var cmd = new SQLiteCommand(create, c);
			cmd.ExecuteNonQuery();
		}

		public static async Task LogAsync(string symbol, string action, decimal price, long qty,
			decimal rsi, decimal macd, decimal macdSignal, decimal atr, decimal sma, decimal ub, decimal lb, decimal equity)
		{
			try
			{
				using var c = new SQLiteConnection(ConnStr);
				await c.OpenAsync();
				using var cmd = new SQLiteCommand(c);
				cmd.CommandText = @"INSERT INTO trades (ts, symbol, action, price, qty, rsi, macd, macd_signal, atr, sma, ub, lb, equity)
                                    VALUES (@ts,@sym,@act,@price,@qty,@rsi,@macd,@msig,@atr,@sma,@ub,@lb,@eq);";
				cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
				cmd.Parameters.AddWithValue("@sym", symbol);
				cmd.Parameters.AddWithValue("@act", action);
				cmd.Parameters.AddWithValue("@price", price);
				cmd.Parameters.AddWithValue("@qty", qty);
				cmd.Parameters.AddWithValue("@rsi", rsi);
				cmd.Parameters.AddWithValue("@macd", macd);
				cmd.Parameters.AddWithValue("@msig", macdSignal);
				cmd.Parameters.AddWithValue("@atr", atr);
				cmd.Parameters.AddWithValue("@sma", sma);
				cmd.Parameters.AddWithValue("@ub", ub);
				cmd.Parameters.AddWithValue("@lb", lb);
				cmd.Parameters.AddWithValue("@eq", equity);
				await cmd.ExecuteNonQueryAsync();
				Console.WriteLine($"Logged trade {action} {symbol} qty={qty} price={price:F2}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Logger error: {ex.Message}");
			}
		}
	}
}
