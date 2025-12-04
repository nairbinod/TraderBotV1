using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Template showing how to implement a Dapper-based data service for SQL Server.
/// This file is a template only: Dapper.SqlClient libraries are not referenced in this environment.
/// To use in production:
/// 1) Add package references: Dapper and Microsoft.Data.SqlClient (or System.Data.SqlClient)
/// 2) Register this service in DI and provide a valid connection string.
/// 3) Uncomment and adapt the code below.
/// </summary>

namespace TraderBotWeb.Services
{
    public class SqlDataService : IDataService
    {
		private readonly string _connectionString = "Server=23.19.249.88,784;Database=nairbinod_lmsone;User Id=nairbinod_sa;Password=33!6Shady;Encrypt=False;TrustServerCertificate=true";

		public SqlDataService(string connectionString ="")
        {
            //_connectionString = connectionString;
        }

        public async Task<IEnumerable<RecommendationModel>> GetRecommendationsAsync(int top = 1000)
        {
            // Example Dapper code (commented out):

            using var conn = new SqlConnection(_connectionString);
            var sql = @"
                SELECT TOP (@Top)
                    [id],
                    [symbol],
                    [signal_timestamp] as SignalTimestamp ,
                    [bar_date] as Bardate,
                    [side],
                    [quantity],
                    [price],
                    [total_value] as TotalValue,
                    [confidence],
                    [quality] * 100 as Quality,
                    [created_at] as CreateAt,
                    [current_price] as Currentprice,
                    [lastupdatedon]
                FROM [dbo].[tb_trades]
                ORDER BY [quality] DESC";
            return await conn.QueryAsync<RecommendationModel>(sql, new { Top = top });

           // throw new NotImplementedException("SqlDataService is a template. Add Dapper and enable code.");
        }

        public async Task<IEnumerable<TradeHistoryModel>> GetTradeHistoryAsync(string symbol, DateTime from, DateTime to)
        {
            // Example Dapper code (commented out):
            
            using var conn = new SqlConnection(_connectionString);
            var sql = @"
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
                FROM dbo.[tb_tradeshistory]
                WHERE [symbol] = @Symbol AND [bar_date] BETWEEN @From AND @To
                ORDER BY [bar_date] ASC ,quality,confidence";
            return await conn.QueryAsync<TradeHistoryModel>(sql, new { Symbol = symbol, From = from, To = to });
        }

        // New: template for fetching all trade history across symbols within a date range
        public async Task<IEnumerable<TradeHistoryModel>> GetAllTradeHistoryAsync(DateTime from, DateTime to)
        {
			// Example Dapper code (commented out):
			
            using var conn = new SqlConnection(_connectionString);
            var sql = @"
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
                FROM dbo.[tb_tradeshistory]
                WHERE [bar_date] BETWEEN @From AND @To
                ORDER BY [bar_date] ASC,quality DESC,confidence DESC";
            return await conn.QueryAsync<TradeHistoryModel>(sql, new { From = from, To = to });
        }

        public async Task<IEnumerable<TradeHistoryDetailModel>> GetTradeHistoryDetailsAsync(int tradeHistoryId)
        {
            // Example Dapper code (commented out):
            using var conn = new SqlConnection(_connectionString);
            var sql = @"
                SELECT
                    [id],
                    [trade_history_id] as TradeHistoryId,
                    [bar_date] as BarDate,
                    [signal_timestamp] as SignalTimestamp ,
                    [price],
                    [created_at] as CreatedAt
                FROM dbo.[tb_tradeshistory_details]
                WHERE [trade_history_id] = @Id
                ORDER BY [bar_date] ASC,quality DESC,confidence DESC";
			return await conn.QueryAsync<TradeHistoryDetailModel>(sql, new { Id = tradeHistoryId });
        }

        public Task SaveSubscriberAsync(SubscriptionModel subscriber)
        {
            // Example Dapper code (commented out):
            /*
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            var sql = @"
                INSERT INTO [tradebot].[subscribers] (email, frequency, accept_terms, created_at)
                VALUES (@Email, @Frequency, @AcceptTerms, @CreatedAt)";
            return conn.ExecuteAsync(sql, subscriber);
            */
            throw new NotImplementedException("SqlDataService is a template. Add Dapper and enable code.");
        }
    }
}