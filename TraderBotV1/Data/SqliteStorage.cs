using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace TraderBotV1.Data
{
	public class SqlServerStorage
	{
		private readonly string _connectionString = "Server=23.19.249.88,784;Database=nairbinod_lmsone;User Id=nairbinod_sa;Password=33!6Shady;Encrypt=False;TrustServerCertificate=true";
		private string _isTest = string.Empty;
		public SqlServerStorage(bool isTestMode)
		{
			if(isTestMode) _isTest = "_test";
		}

		private void InitializeDatabase()
		{
			using var conn = new SqlConnection(_connectionString);
			conn.Open();

			// Prices table
			conn.Execute(@"
				IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tb_prices')
				BEGIN
					CREATE TABLE prices (
						id INT PRIMARY KEY IDENTITY(1,1),
						symbol NVARCHAR(20) NOT NULL,
						timestamp DATETIME2 NOT NULL,
						[open] DECIMAL(18,4) NOT NULL,
						high DECIMAL(18,4) NOT NULL,
						low DECIMAL(18,4) NOT NULL,
						[close] DECIMAL(18,4) NOT NULL,
						volume BIGINT NOT NULL,
						created_at DATETIME2 DEFAULT GETDATE()
					)
				END");

			// Signals table
			conn.Execute(@"
				IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tb_signals')
				BEGIN
					CREATE TABLE signals (
						id INT PRIMARY KEY IDENTITY(1,1),
						symbol NVARCHAR(20) NOT NULL,
						timestamp DATETIME2 NOT NULL,
						strategy NVARCHAR(100) NOT NULL,
						signal NVARCHAR(50) NOT NULL,
						reason NVARCHAR(MAX),
						created_at DATETIME2 DEFAULT GETDATE()
					)
				END");

			// Trades table with bar_date
			conn.Execute(@"
				IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tb_trades')
				BEGIN
					CREATE TABLE trades (
						id INT PRIMARY KEY IDENTITY(1,1),
						symbol NVARCHAR(20) NOT NULL,
						signal_timestamp DATETIME2 NOT NULL,
						bar_date DATETIME2,
						side NVARCHAR(10) NOT NULL,
						quantity BIGINT NOT NULL,
						price DECIMAL(18,4) NOT NULL,
						total_value DECIMAL(18,4),
						created_at DATETIME2 DEFAULT GETDATE()
					)
				END");

			// Create indices for better query performance
			conn.Execute(@"
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_prices_symbol')
					CREATE INDEX idx_prices_symbol ON tb_prices(symbol);
				
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_prices_timestamp')
					CREATE INDEX idx_prices_timestamp ON tb_prices(timestamp);
				
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_signals_symbol')
					CREATE INDEX idx_signals_symbol ON tb_signals(symbol);
				
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_signals_timestamp')
					CREATE INDEX idx_signals_timestamp ON tb_signals(timestamp);
				
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_trades_symbol')
					CREATE INDEX idx_trades_symbol ON tb_trades(symbol);
				
				IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_trades_bar_date')
					CREATE INDEX idx_trades_bar_date ON tb_trades(bar_date);
			");
		}

		// ═══════════════════════════════════════════════════════════════
		// Price Data Methods
		// ═══════════════════════════════════════════════════════════════

		public void InsertPrice(string symbol, DateTime timestamp, decimal open, decimal high,
			decimal low, decimal close, long volume)
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = $@"
				INSERT INTO tb_prices{_isTest} (symbol, timestamp, [open], high, low, [close], volume)
				VALUES (@symbol, @timestamp, @open, @high, @low, @close, @volume)";

			conn.Execute(sql, new
			{
				symbol,
				timestamp,
				open,
				high,
				low,
				close,
				volume
			});
		}

		public List<PriceRecord> GetPrices(string symbol, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqlConnection(_connectionString);

			var whereClauses = new List<string> { "symbol = @symbol" };
			var parameters = new DynamicParameters();
			parameters.Add("symbol", symbol);

			if (startDate.HasValue)
			{
				whereClauses.Add("timestamp >= @startDate");
				parameters.Add("startDate", startDate.Value);
			}
			if (endDate.HasValue)
			{
				whereClauses.Add("timestamp <= @endDate");
				parameters.Add("endDate", endDate.Value);
			}

			var sql = $@"
				SELECT id, symbol, timestamp, [open], high, low, [close], volume, created_at
				FROM tb_prices{_isTest}
				WHERE {string.Join(" AND ", whereClauses)}
				ORDER BY timestamp ASC";

			return conn.Query<PriceRecord>(sql, parameters).ToList();
		}

		// ═══════════════════════════════════════════════════════════════
		// Signal Methods
		// ═══════════════════════════════════════════════════════════════

		public void InsertSignal(string symbol, DateTime timestamp, string strategy, string signal, string? reason = null)
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = $@"
				INSERT INTO tb_signals{_isTest} (symbol, timestamp, strategy, signal, reason)
				VALUES (@symbol, @timestamp, @strategy, @signal, @reason)";

			conn.Execute(sql, new
			{
				symbol,
				timestamp,
				strategy,
				signal,
				reason
			});
		}

		public List<SignalRecord> GetSignals(string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqlConnection(_connectionString);

			var whereClauses = new List<string>();
			var parameters = new DynamicParameters();

			if (!string.IsNullOrWhiteSpace(symbol))
			{
				whereClauses.Add("symbol = @symbol");
				parameters.Add("symbol", symbol);
			}
			if (startDate.HasValue)
			{
				whereClauses.Add("timestamp >= @startDate");
				parameters.Add("startDate", startDate.Value);
			}
			if (endDate.HasValue)
			{
				whereClauses.Add("timestamp <= @endDate");
				parameters.Add("endDate", endDate.Value);
			}

			var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

			var sql = $@"
				SELECT id, symbol, timestamp, strategy, signal, reason, created_at
				FROM tb_signals{_isTest}
				{whereClause}
				ORDER BY timestamp DESC";

			return conn.Query<SignalRecord>(sql, parameters).ToList();
		}

		// ═══════════════════════════════════════════════════════════════
		// Trade Methods (UPDATED with bar_date)
		// ═══════════════════════════════════════════════════════════════

		public void InsertTrade(string symbol, DateTime signalTimestamp, DateTime? barDate, string side,
			long quantity, decimal price, decimal totalValue, decimal confidence, decimal quality)
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = $@"
				INSERT INTO tb_trades{_isTest} (symbol, signal_timestamp, bar_date, side, quantity, price, total_value,confidence,quality)
				VALUES (@symbol, @signalTimestamp, @barDate, @side, @quantity, @price, @totalValue, @confidence,@quality)";

			conn.Execute(sql, new
			{
				symbol,
				signalTimestamp,
				barDate,
				side,
				quantity,
				price,
				totalValue,
				confidence,
				quality
			});
		}

		public List<TradeRecord> GetTrades(string? symbol = null, DateTime? startDate = null,
			DateTime? endDate = null, string? side = null)
		{
			using var conn = new SqlConnection(_connectionString);

			var whereClauses = new List<string>();
			var parameters = new DynamicParameters();

			if (!string.IsNullOrWhiteSpace(symbol))
			{
				whereClauses.Add("symbol = @symbol");
				parameters.Add("symbol", symbol);
			}
			if (startDate.HasValue)
			{
				whereClauses.Add("bar_date >= @startDate");
				parameters.Add("startDate", startDate.Value);
			}
			if (endDate.HasValue)
			{
				whereClauses.Add("bar_date <= @endDate");
				parameters.Add("endDate", endDate.Value);
			}
			if (!string.IsNullOrWhiteSpace(side))
			{
				whereClauses.Add("side = @side");
				parameters.Add("side", side);
			}

			var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

			var sql = $@"
				SELECT id, symbol, signal_timestamp, bar_date, side, quantity, price, total_value, created_at
				FROM dbo.tb_trades{_isTest}
				{whereClause}
				ORDER BY bar_date DESC, signal_timestamp DESC";

			return conn.Query<TradeRecord>(sql, parameters).ToList();
		}

		// ═══════════════════════════════════════════════════════════════
		// Utility Methods
		// ═══════════════════════════════════════════════════════════════

		public List<string> GetActiveSymbols()
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = @"
				SELECT DISTINCT symbol 
				FROM dbo.tb_symbols 
				WHERE isActive = 1
				ORDER BY symbol";

			return conn.Query<string>(sql).ToList();
		}

		public List<string> GetActiveSubscribers()
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = $@"SELECT DISTINCT email FROM tb_subscribers{_isTest};";

			// Fix: Use QueryFirstOrDefault to retrieve a single string value
			return conn.Query<string>(sql).ToList();
		}

		public async Task<IEnumerable<TradeRecord>> GetTradeHistory()//DateTime from, DateTime to)
		{
			// Example Dapper code (commented out):

			using var conn = new SqlConnection(_connectionString);
			var sql = $@"
				SELECT
				   [id]
				  ,[symbol]
				  ,[signal_timestamp] SignalTimestamp
				  ,[bar_date] BarDate 
				  ,[side]
				  ,[quantity]
				  ,[price]
				  ,[total_value] TotalValue
				  ,[confidence]
				  ,[quality]
				  ,[created_at] CreatedAt
				  ,[current_price] CurrentPrice
				  ,[lastupdatedon]
				FROM dbo.[tb_tradeshistory]{_isTest}
				WHERE isActive is null or isActive = 1 
				ORDER BY [bar_date] ASC,quality DESC,confidence DESC";
			//[bar_date] BETWEEN @From AND @To
			return await conn.QueryAsync<TradeRecord>(sql);//, new { From = from, To = to });
		}

		public void InsertTradeHistoryDetail(int tradeHistoryId,
												string symbol,
												DateTime? timestamp,
												DateTime? barDate,
												decimal price)
		{
			using var conn = new SqlConnection(_connectionString);

			conn.Execute($"InsertTradeHistoryDetail{_isTest}",
													new
													{
														tradeHistoryId,
														symbol,
														timestamp,
														barDate,
														price,
														created_at = DateTime.Now
													},
													commandType: CommandType.StoredProcedure
			);
		}
		public void UpdateCurrentValue(string symbol, decimal current_price)
		{
			using var conn = new SqlConnection(_connectionString);

			var sql = $@"
				UPDATE dbo.tb_TradesHistory{_isTest} 
				SET current_price = @current_price, 
					lastupdatedon = @lastupdatedon 
				WHERE symbol = @symbol";

			conn.Execute(sql, new
			{
				current_price,
				lastupdatedon = DateTime.Now,
				symbol
			});
		}

		/// <summary>
		/// Get trade statistics for a symbol
		/// </summary>
		public TradeStatistics GetTradeStatistics(string symbol, DateTime? startDate = null, DateTime? endDate = null)
		{
			using var conn = new SqlConnection(_connectionString);

			var whereClauses = new List<string> { "symbol = @symbol" };
			var parameters = new DynamicParameters();
			parameters.Add("symbol", symbol);

			if (startDate.HasValue)
			{
				whereClauses.Add("bar_date >= @startDate");
				parameters.Add("startDate", startDate.Value);
			}
			if (endDate.HasValue)
			{
				whereClauses.Add("bar_date <= @endDate");
				parameters.Add("endDate", endDate.Value);
			}

			var sql = $@"
				SELECT 
					COUNT(*) as TotalTrades,
					SUM(CASE WHEN side = 'Buy' THEN 1 ELSE 0 END) as BuyCount,
					SUM(CASE WHEN side = 'Sell' THEN 1 ELSE 0 END) as SellCount,
					ISNULL(SUM(total_value), 0) as TotalVolume,
					ISNULL(AVG(price), 0) as AvgPrice,
					ISNULL(MIN(price), 0) as MinPrice,
					ISNULL(MAX(price), 0) as MaxPrice
				FROM dbo.tb_trades{_isTest}
				WHERE {string.Join(" AND ", whereClauses)}";

			var stats = conn.QueryFirstOrDefault<TradeStatistics>(sql, parameters);

			if (stats != null)
			{
				stats.Symbol = symbol;
				return stats;
			}

			return new TradeStatistics { Symbol = symbol };
		}

		/// <summary>
		/// Clear all data (use with caution!)
		/// </summary>
		public void ClearAllData()
		{
			using var conn = new SqlConnection(_connectionString);
			conn.Execute($@"dbo.ResetData{_isTest}", commandType: CommandType.StoredProcedure);
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
		public decimal Confidence { get; set; }
		public decimal Quality { get; set; }
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