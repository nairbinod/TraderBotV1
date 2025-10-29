using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace TraderBotV1.Data
{
	public class SqliteStorage
	{
		private readonly string _connectionString;
		private const string DB_FILE = "TraderBot.v1.db";

		public SqliteStorage(string? dbPath = null)
		{
			dbPath = @"C:\github\nairbinod\TraderBotV1\TraderBotV1\Data\TraderBot.v1.db";
			_connectionString = $"Data Source={dbPath ?? DB_FILE}";
			InitializeDatabase();
		}

		private void InitializeDatabase()
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();

			// Prices table
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS prices (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    open REAL NOT NULL,
                    high REAL NOT NULL,
                    low REAL NOT NULL,
                    close REAL NOT NULL,
                    volume INTEGER NOT NULL,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                )";
			cmd.ExecuteNonQuery();

			// Signals table
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS signals (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    strategy TEXT NOT NULL,
                    signal TEXT NOT NULL,
                    reason TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                )";
			cmd.ExecuteNonQuery();

			// Updated trades table with bar_date
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS trades (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    signal_timestamp TEXT NOT NULL,
                    bar_date TEXT,
                    side TEXT NOT NULL,
                    quantity INTEGER NOT NULL,
                    price REAL NOT NULL,
                    total_value REAL,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                )";
			cmd.ExecuteNonQuery();

			// Create indices for better query performance
			cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_prices_symbol ON prices(symbol);
                CREATE INDEX IF NOT EXISTS idx_prices_timestamp ON prices(timestamp);
                CREATE INDEX IF NOT EXISTS idx_signals_symbol ON signals(symbol);
                CREATE INDEX IF NOT EXISTS idx_signals_timestamp ON signals(timestamp);
                CREATE INDEX IF NOT EXISTS idx_trades_symbol ON trades(symbol);
                CREATE INDEX IF NOT EXISTS idx_trades_bar_date ON trades(bar_date);
            ";
			cmd.ExecuteNonQuery();
		}

		// ═══════════════════════════════════════════════════════════════
		// Price Data Methods
		// ═══════════════════════════════════════════════════════════════

		public void InsertPrice(string symbol, DateTime timestamp, decimal open, decimal high,
			decimal low, decimal close, long volume)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			cmd.CommandText = @"
                INSERT INTO prices (symbol, timestamp, open, high, low, close, volume)
                VALUES (@symbol, @timestamp, @open, @high, @low, @close, @volume)";

			cmd.Parameters.AddWithValue("@symbol", symbol);
			cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.Parameters.AddWithValue("@open", (double)open);
			cmd.Parameters.AddWithValue("@high", (double)high);
			cmd.Parameters.AddWithValue("@low", (double)low);
			cmd.Parameters.AddWithValue("@close", (double)close);
			cmd.Parameters.AddWithValue("@volume", volume);

			cmd.ExecuteNonQuery();
		}

		public List<PriceRecord> GetPrices(string symbol, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			var whereClauses = new List<string> { "symbol = @symbol" };

			if (startDate.HasValue)
				whereClauses.Add("timestamp >= @startDate");
			if (endDate.HasValue)
				whereClauses.Add("timestamp <= @endDate");

			cmd.CommandText = $@"
                SELECT id, symbol, timestamp, open, high, low, close, volume, created_at
                FROM prices
                WHERE {string.Join(" AND ", whereClauses)}
                ORDER BY timestamp ASC";

			cmd.Parameters.AddWithValue("@symbol", symbol);
			if (startDate.HasValue)
				cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
			if (endDate.HasValue)
				cmd.Parameters.AddWithValue("@endDate", endDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));

			var prices = new List<PriceRecord>();
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
			{
				prices.Add(new PriceRecord
				{
					Id = reader.GetInt32(0),
					Symbol = reader.GetString(1),
					Timestamp = DateTime.Parse(reader.GetString(2)),
					Open = (decimal)reader.GetDouble(3),
					High = (decimal)reader.GetDouble(4),
					Low = (decimal)reader.GetDouble(5),
					Close = (decimal)reader.GetDouble(6),
					Volume = reader.GetInt64(7),
					CreatedAt = DateTime.Parse(reader.GetString(8))
				});
			}

			return prices;
		}

		// ═══════════════════════════════════════════════════════════════
		// Signal Methods
		// ═══════════════════════════════════════════════════════════════

		public void InsertSignal(string symbol, DateTime timestamp, string strategy, string signal, string? reason = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			cmd.CommandText = @"
                INSERT INTO signals (symbol, timestamp, strategy, signal, reason)
                VALUES (@symbol, @timestamp, @strategy, @signal, @reason)";

			cmd.Parameters.AddWithValue("@symbol", symbol);
			cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.Parameters.AddWithValue("@strategy", strategy);
			cmd.Parameters.AddWithValue("@signal", signal);
			cmd.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);

			cmd.ExecuteNonQuery();
		}

		public List<SignalRecord> GetSignals(string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			var whereClauses = new List<string>();

			if (!string.IsNullOrWhiteSpace(symbol))
				whereClauses.Add("symbol = @symbol");
			if (startDate.HasValue)
				whereClauses.Add("timestamp >= @startDate");
			if (endDate.HasValue)
				whereClauses.Add("timestamp <= @endDate");

			var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

			cmd.CommandText = $@"
                SELECT id, symbol, timestamp, strategy, signal, reason, created_at
                FROM signals
                {whereClause}
                ORDER BY timestamp DESC";

			if (!string.IsNullOrWhiteSpace(symbol))
				cmd.Parameters.AddWithValue("@symbol", symbol);
			if (startDate.HasValue)
				cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
			if (endDate.HasValue)
				cmd.Parameters.AddWithValue("@endDate", endDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));

			var signals = new List<SignalRecord>();
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
			{
				signals.Add(new SignalRecord
				{
					Id = reader.GetInt32(0),
					Symbol = reader.GetString(1),
					Timestamp = DateTime.Parse(reader.GetString(2)),
					Strategy = reader.GetString(3),
					Signal = reader.GetString(4),
					Reason = reader.IsDBNull(5) ? null : reader.GetString(5),
					CreatedAt = DateTime.Parse(reader.GetString(6))
				});
			}

			return signals;
		}

		// ═══════════════════════════════════════════════════════════════
		// Trade Methods (UPDATED with bar_date)
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Insert a trade record with optional bar date
		/// </summary>
		public void InsertTrade(string symbol, DateTime signalTimestamp, string side,
			long quantity, decimal price, DateTime? barDate = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			cmd.CommandText = @"
                INSERT INTO trades (symbol, signal_timestamp, bar_date, side, quantity, price, total_value)
                VALUES (@symbol, @signal_timestamp, @bar_date, @side, @quantity, @price, @total_value)";

			cmd.Parameters.AddWithValue("@symbol", symbol);
			cmd.Parameters.AddWithValue("@signal_timestamp", signalTimestamp.ToString("yyyy-MM-dd HH:mm:ss"));
			cmd.Parameters.AddWithValue("@bar_date", barDate.HasValue
				? barDate.Value.ToString("yyyy-MM-dd HH:mm:ss")
				: (object)DBNull.Value);
			cmd.Parameters.AddWithValue("@side", side);
			cmd.Parameters.AddWithValue("@quantity", quantity);
			cmd.Parameters.AddWithValue("@price", (double)price);
			cmd.Parameters.AddWithValue("@total_value", (double)(quantity * price));

			cmd.ExecuteNonQuery();
		}

		/// <summary>
		/// Get trades with optional filtering
		/// </summary>
		public List<TradeRecord> GetTrades(string? symbol = null, DateTime? startDate = null,
			DateTime? endDate = null, string? side = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			var whereClauses = new List<string>();

			if (!string.IsNullOrWhiteSpace(symbol))
				whereClauses.Add("symbol = @symbol");
			if (startDate.HasValue)
				whereClauses.Add("bar_date >= @startDate");
			if (endDate.HasValue)
				whereClauses.Add("bar_date <= @endDate");
			if (!string.IsNullOrWhiteSpace(side))
				whereClauses.Add("side = @side");

			var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

			cmd.CommandText = $@"
                SELECT id, symbol, signal_timestamp, bar_date, side, quantity, price, total_value, created_at
                FROM trades
                {whereClause}
                ORDER BY bar_date DESC, signal_timestamp DESC";

			if (!string.IsNullOrWhiteSpace(symbol))
				cmd.Parameters.AddWithValue("@symbol", symbol);
			if (startDate.HasValue)
				cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
			if (endDate.HasValue)
				cmd.Parameters.AddWithValue("@endDate", endDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
			if (!string.IsNullOrWhiteSpace(side))
				cmd.Parameters.AddWithValue("@side", side);

			var trades = new List<TradeRecord>();
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
			{
				trades.Add(new TradeRecord
				{
					Id = reader.GetInt32(0),
					Symbol = reader.GetString(1),
					SignalTimestamp = DateTime.Parse(reader.GetString(2)),
					BarDate = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
					Side = reader.GetString(4),
					Quantity = reader.GetInt64(5),
					Price = (decimal)reader.GetDouble(6),
					TotalValue = (decimal)reader.GetDouble(7),
					CreatedAt = DateTime.Parse(reader.GetString(8))
				});
			}

			return trades;
		}

		// ═══════════════════════════════════════════════════════════════
		// Utility Methods
		// ═══════════════════════════════════════════════════════════════

		public List<string> GetActiveSymbols()
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			cmd.CommandText = @"
                SELECT DISTINCT symbol 
                FROM symbols 
                WHERE Active = 1
                ORDER BY symbol";

			var symbols = new List<string>();
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
			{
				symbols.Add(reader.GetString(0));
			}

			return symbols;
		}

		/// <summary>
		/// Get trade statistics for a symbol
		/// </summary>
		public TradeStatistics GetTradeStatistics(string symbol, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			var whereClauses = new List<string> { "symbol = @symbol" };

			if (startDate.HasValue)
				whereClauses.Add("bar_date >= @startDate");
			if (endDate.HasValue)
				whereClauses.Add("bar_date <= @endDate");

			cmd.CommandText = $@"
                SELECT 
                    COUNT(*) as total_trades,
                    SUM(CASE WHEN side = 'Buy' THEN 1 ELSE 0 END) as buy_count,
                    SUM(CASE WHEN side = 'Sell' THEN 1 ELSE 0 END) as sell_count,
                    SUM(total_value) as total_volume,
                    AVG(price) as avg_price,
                    MIN(price) as min_price,
                    MAX(price) as max_price
                FROM trades
                WHERE {string.Join(" AND ", whereClauses)}";

			cmd.Parameters.AddWithValue("@symbol", symbol);
			if (startDate.HasValue)
				cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
			if (endDate.HasValue)
				cmd.Parameters.AddWithValue("@endDate", endDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));

			using var reader = cmd.ExecuteReader();

			if (reader.Read())
			{
				return new TradeStatistics
				{
					Symbol = symbol,
					TotalTrades = reader.GetInt32(0),
					BuyCount = reader.GetInt32(1),
					SellCount = reader.GetInt32(2),
					TotalVolume = reader.IsDBNull(3) ? 0m : (decimal)reader.GetDouble(3),
					AvgPrice = reader.IsDBNull(4) ? 0m : (decimal)reader.GetDouble(4),
					MinPrice = reader.IsDBNull(5) ? 0m : (decimal)reader.GetDouble(5),
					MaxPrice = reader.IsDBNull(6) ? 0m : (decimal)reader.GetDouble(6)
				};
			}

			return new TradeStatistics { Symbol = symbol };
		}

		/// <summary>
		/// Clear all data (use with caution!)
		/// </summary>
		public void ClearAllData()
		{
			using var conn = new SqliteConnection(_connectionString);
			conn.Open();

			var cmd = conn.CreateCommand();
			cmd.CommandText = "DELETE FROM prices; DELETE FROM signals; DELETE FROM trades;";
			cmd.ExecuteNonQuery();

			Console.WriteLine("⚠️ All data cleared from database");
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Data Models
	// ═══════════════════════════════════════════════════════════════

	public class PriceRecord
	{
		public int Id { get; set; }
		public string Symbol { get; set; } = "";
		public DateTime Timestamp { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public long Volume { get; set; }
		public DateTime CreatedAt { get; set; }
	}

	public class SignalRecord
	{
		public int Id { get; set; }
		public string Symbol { get; set; } = "";
		public DateTime Timestamp { get; set; }
		public string Strategy { get; set; } = "";
		public string Signal { get; set; } = "";
		public string? Reason { get; set; }
		public DateTime CreatedAt { get; set; }
	}

	public class TradeRecord
	{
		public int Id { get; set; }
		public string Symbol { get; set; } = "";
		public DateTime SignalTimestamp { get; set; }
		public DateTime? BarDate { get; set; }  // NEW: Date from the price bar
		public string Side { get; set; } = "";
		public long Quantity { get; set; }
		public decimal Price { get; set; }
		public decimal TotalValue { get; set; }
		public DateTime CreatedAt { get; set; }
	}

	public class TradeStatistics
	{
		public string Symbol { get; set; } = "";
		public int TotalTrades { get; set; }
		public int BuyCount { get; set; }
		public int SellCount { get; set; }
		public decimal TotalVolume { get; set; }
		public decimal AvgPrice { get; set; }
		public decimal MinPrice { get; set; }
		public decimal MaxPrice { get; set; }
	}
}