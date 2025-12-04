using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Mock implementation for demo purposes. Swap with SqlDataService in production.
/// </summary>
/// 
namespace TraderBotWeb.Services
{
    public class MockDataService : IDataService
    {
        public Task<IEnumerable<RecommendationModel>> GetRecommendationsAsync(int top = 1000)
        {
            var list = new List<RecommendationModel>
        {
            new RecommendationModel
            {
                Id = 1,
                //DayLabel = "Today",
                Symbol = "AAPL",
                Market = "NASDAQ",
                Confidence = 0.87m,
                //Quality = "High",
                SignalTimestamp = DateTime.Now,
                BarDate = DateTime.Today,
                Side = "Buy",
                Quantity = 10,
                Price = 172.45m,
                TotalValue = 1724.5m,
                CurrentPrice = 173.00m,
                CreatedAt = DateTime.Now
            },
            new RecommendationModel
            {
                Id = 2,
                //DayLabel = "Today",
                Symbol = "MSFT",
                Market = "NASDAQ",
                Confidence = 0.81m,
                //Quality = "High",
                SignalTimestamp = DateTime.Now,
                BarDate = DateTime.Today,
                Side = "Buy",
                Quantity = 5,
                Price = 310.10m,
                TotalValue = 1550.5m,
                CurrentPrice = 311.00m,
                CreatedAt = DateTime.Now
            },
            new RecommendationModel
            {
                Id = 3,
                //DayLabel = "Today",
                Symbol = "TSLA",
                Market = "NASDAQ",
                Confidence = 0.72m,
                //Quality = "Medium",
                SignalTimestamp = DateTime.Now,
                BarDate = DateTime.Today,
                Side = "Buy",
                Quantity = 2,
                Price = 220.00m,
                TotalValue = 440.00m,
                CurrentPrice = 221.00m,
                CreatedAt = DateTime.Now
            }
        };

            return Task.FromResult<IEnumerable<RecommendationModel>>(list.Take(top));
        }

        public Task<IEnumerable<TradeHistoryModel>> GetTradeHistoryAsync(string symbol, DateTime from, DateTime to)
        {
            // Generate sample historic data for demo
            var rand = new Random(symbol.GetHashCode());
            var days = (to - from).Days + 1;
            var history = new List<TradeHistoryModel>();

            for (int i = 0; i < Math.Min(60, Math.Max(0, days)); i++)
            {
                var date = from.AddDays(i);
                var price = Math.Round(100 + rand.NextDouble() * 200, 2);
                var qty = rand.Next(1, 10);

                // Simulate a current price near the original price with small random movement
                var currentPriceDelta = Math.Round((rand.NextDouble() - 0.5) * 10, 2); // -5 .. +5
                var currentPrice = Math.Round(price + currentPriceDelta, 2);

                history.Add(new TradeHistoryModel
                {
                    Id = i + 1,
                    Symbol = symbol,
                    BarDate = date,
                    Price = (decimal)price,
                    CurrentPrice = (decimal)currentPrice,
                    Quantity = qty,
                    TotalValue = (decimal)price * qty,
                    Confidence = (decimal)(0.5 + rand.NextDouble() * 0.5),
                    //Quality = (rand.NextDouble() > 0.7) ? "High" : "Medium",
                    CreatedAt = DateTime.UtcNow
                });
            }

            return Task.FromResult<IEnumerable<TradeHistoryModel>>(history);
        }

        public Task<IEnumerable<TradeHistoryDetailModel>> GetTradeHistoryDetailsAsync(int tradeHistoryId)
        {
            // Create mock daily details tied to the master tradeHistoryId
            var rand = new Random(tradeHistoryId);
            var details = new List<TradeHistoryDetailModel>();

            // Return up to 14 days of daily OHLC data for demo
            for (int i = 0; i < 14; i++)
            {
                var date = DateTime.Today.AddDays(-i);
                var basePrice = Math.Round(100 + rand.NextDouble() * 200, 2);
                var high = basePrice + Math.Round(rand.NextDouble() * 5, 2);
                var low = basePrice - Math.Round(rand.NextDouble() * 5, 2);
                var open = Math.Round(basePrice + (rand.NextDouble() - 0.5) * 2, 2);
                var close = Math.Round(basePrice + (rand.NextDouble() - 0.5) * 2, 2);
                var vol = rand.Next(1000, 100000);

                details.Add(new TradeHistoryDetailModel
                {
                    Id = i + 1,
                    TradeHistoryId = tradeHistoryId,
                    BarDate = date,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return Task.FromResult<IEnumerable<TradeHistoryDetailModel>>(details);
        }

        // New: return all trades across symbols within a date range (mocked)
        public async Task<IEnumerable<TradeHistoryModel>> GetAllTradeHistoryAsync(DateTime from, DateTime to)
        {
            var all = new List<TradeHistoryModel>();

            // Use recommendations as a source of symbols for demo
            var recs = (await GetRecommendationsAsync(1000)).ToList();
            var symbols = recs.Select(r => r.Symbol).Distinct().ToList();

            // If no recommendations exist, include a few defaults
            if (!symbols.Any())
            {
                symbols = new List<string> { "AAPL", "MSFT", "TSLA", "GOOG" };
            }

            int idCounter = 1;
            foreach (var s in symbols)
            {
                var hist = (await GetTradeHistoryAsync(s, from, to)).ToList();
                foreach (var h in hist)
                {
                    // Ensure unique Id across aggregated list
                    h.Id = idCounter++;
                    all.Add(h);
                }
            }

            // Order by date descending for master list UX
            return all.OrderByDescending(h => h.BarDate).ToList();
        }

        public Task SaveSubscriberAsync(SubscriptionModel subscriber)
        {
            // In demo mode just log to console (no external libs). In a real app persist to DB.
            Console.WriteLine($"[Mock] Saving subscriber {subscriber.Email} - Frequency: {subscriber.Frequency}");
            subscriber.CreatedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }
}