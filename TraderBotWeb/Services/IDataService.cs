using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace TraderBotWeb.Services
{
    public interface IDataService
    {
        Task<IEnumerable<RecommendationModel>> GetRecommendationsAsync(int top = 1000);
        Task<IEnumerable<TradeHistoryModel>> GetTradeHistoryAsync(string symbol, DateTime from, DateTime to);

        // New: fetch all trade history across symbols for a date range
        Task<IEnumerable<TradeHistoryModel>> GetAllTradeHistoryAsync(DateTime from, DateTime to);

        Task<IEnumerable<TradeHistoryDetailModel>> GetTradeHistoryDetailsAsync(int tradeHistoryId);
        Task<int> SaveSubscriberAsync(SubscriptionModel subscriber);
        Task<IEnumerable<PredictionPerformanceModel>> GetPredictionPerformanceAsync(DateTime from, DateTime to);
    }
}