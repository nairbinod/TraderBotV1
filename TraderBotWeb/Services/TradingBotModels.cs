using System.ComponentModel.DataAnnotations;
namespace TraderBotWeb.Services
{
    public static class Helper
    {
        public static string ConvertUtcToCentral(DateTime utcDateTime)
        {
            var centralZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, centralZone).ToString();
        }
    }

    public class RecommendationModel
    {
        public int Id { get; set; } // maps to [id]
        public string DayLabel
        {
            get
            {
                return Helper.ConvertUtcToCentral(SignalTimestamp);
            }
        }
        public string Symbol { get; set; } = string.Empty;
        public string Market { get; set; } = string.Empty;

        // Confidence between 0 and 1
        public decimal Confidence { get; set; }

        // Example values: High, Medium, Low
        public decimal Quality { get; set; }

        // additional fields from the trades table
        public DateTime SignalTimestamp { get; set; }
        public DateTime BarDate { get; set; }
        public string Side { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TotalValue { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTime? LastUpdatedOn { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TradeHistoryModel
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Confidence { get; set; }

        // Example values: High, Medium, Low
        public decimal Quality { get; set; }

        // additional fields from the trades table
        public DateTime SignalTimestamp { get; set; }
        public DateTime BarDate { get; set; }
        public string Side { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TotalValue { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTime? LastUpdatedOn { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TradeHistoryDetailModel
    {
        // Represents a daily detail row coming from TradeHistoryDetails table
        public int Id { get; set; }
        public int TradeHistoryId { get; set; } // foreign key to TradeHistory.Id
        public string Symbol { get; set; } = string.Empty;
        public decimal RecommendedPrice { get; set; }
        public decimal Confidence { get; set; }
        public int Quality { get; set; }
        public DateTime BarDate { get; set; }
        public decimal Price { get; set; }
        public DateTime SignalTimestamp { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class SubscriptionModel
    {
        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Frequency { get; set; } = "Daily";

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms to subscribe.")]
        public bool AcceptTerms { get; set; }

        // For DB persistence
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PredictionPerformanceModel
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public DateTime BarDate { get; set; }
        public DateTime SignalTimestamp { get; set; }
        public decimal RecommendedPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal Confidence { get; set; }
        public decimal Quality { get; set; }
        public int Quantity { get; set; }

        public decimal GainLossAmount => CurrentPrice - RecommendedPrice;
        public decimal GainLossPercent => RecommendedPrice > 0
            ? (CurrentPrice - RecommendedPrice) / RecommendedPrice
            : 0;
        public bool IsWinner => CurrentPrice > RecommendedPrice;
    }

    public class WhatIfResultModel
    {
        public decimal InitialInvestment { get; set; }
        public decimal FinalValue { get; set; }
        public decimal TotalGainLoss => FinalValue - InitialInvestment;
        public decimal TotalReturnPercent => InitialInvestment > 0
            ? (FinalValue - InitialInvestment) / InitialInvestment
            : 0;
        public int TotalTrades { get; set; }
        public decimal AverageInvestmentPerTrade { get; set; }
        public List<WhatIfTradeDetail> TradeDetails { get; set; } = new();
    }

    public class WhatIfTradeDetail
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime BarDate { get; set; }
        public decimal InvestmentAmount { get; set; }
        public decimal RecommendedPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal SharesPurchased { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal GainLoss { get; set; }
        public decimal GainLossPercent { get; set; }
    }
}