using Microsoft.AspNetCore.Components;
using TraderBotWeb.Services;

namespace TraderBotWeb.Components.Pages
{
    public partial class PredictionPerformance : ComponentBase
    {
        private readonly IDataService dataService = new SqlDataService();

        // Date range filters
        private DateTime dateFrom = DateTime.Today.AddDays(-20);
        private DateTime dateTo = DateTime.Today;

        // Data state
        private List<PredictionPerformanceModel> predictions = new();
        private bool isDataLoaded = false;
        private bool isLoading = false;

        // Calculated statistics
        private int winningCount = 0;
        private int losingCount = 0;
        private decimal winRate = 0;
        private decimal avgGainLossPercent = 0;
        private decimal bestPerformingPercent = 0;
        private decimal worstPerformingPercent = 0;
        private string bestPerformingSymbol = "";
        private string worstPerformingSymbol = "";

        // What-if calculator state
        private decimal investmentAmount = 10000m;
        private string allocationStrategy = "equal";
        private WhatIfResultModel? whatIfResult = null;
        private bool showWhatIfDetails = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadPerformanceData();
        }

        private void SetDateRange(int days)
        {
            dateTo = DateTime.Today;
            dateFrom = DateTime.Today.AddDays(-days);
            StateHasChanged();
        }

        private async Task LoadPerformanceData()
        {
            isLoading = true;
            isDataLoaded = false;
            whatIfResult = null;
            showWhatIfDetails = false;
            StateHasChanged();

            predictions = (await dataService.GetPredictionPerformanceAsync(dateFrom, dateTo)).ToList();
            CalculateStatistics();

            isDataLoaded = true;
            isLoading = false;
            StateHasChanged();
        }

        private void CalculateStatistics()
        {
            if (predictions.Count == 0)
            {
                winningCount = 0;
                losingCount = 0;
                winRate = 0;
                avgGainLossPercent = 0;
                bestPerformingPercent = 0;
                worstPerformingPercent = 0;
                bestPerformingSymbol = "";
                worstPerformingSymbol = "";
                return;
            }

            winningCount = predictions.Count(p => p.IsWinner);
            losingCount = predictions.Count - winningCount;
            winRate = (decimal)winningCount / predictions.Count;
            avgGainLossPercent = predictions.Average(p => p.GainLossPercent);

            var best = predictions.OrderByDescending(p => p.GainLossPercent).First();
            var worst = predictions.OrderBy(p => p.GainLossPercent).First();

            bestPerformingPercent = best.GainLossPercent;
            bestPerformingSymbol = best.Symbol;
            worstPerformingPercent = worst.GainLossPercent;
            worstPerformingSymbol = worst.Symbol;
        }

        private void CalculateWhatIf()
        {
            if (predictions.Count == 0 || investmentAmount <= 0)
                return;

            var result = new WhatIfResultModel
            {
                InitialInvestment = investmentAmount,
                TotalTrades = predictions.Count,
                TradeDetails = new List<WhatIfTradeDetail>()
            };

            decimal totalFinalValue = 0;

            if (allocationStrategy == "equal")
            {
                decimal perTradeAmount = investmentAmount / predictions.Count;
                result.AverageInvestmentPerTrade = perTradeAmount;

                foreach (var p in predictions)
                {
                    var sharesPurchased = perTradeAmount / p.RecommendedPrice;
                    var currentValue = sharesPurchased * p.CurrentPrice;
                    var gainLoss = currentValue - perTradeAmount;
                    var gainLossPercent = perTradeAmount > 0 ? gainLoss / perTradeAmount : 0;

                    result.TradeDetails.Add(new WhatIfTradeDetail
                    {
                        Symbol = p.Symbol,
                        BarDate = p.BarDate,
                        InvestmentAmount = perTradeAmount,
                        RecommendedPrice = p.RecommendedPrice,
                        CurrentPrice = p.CurrentPrice,
                        SharesPurchased = sharesPurchased,
                        CurrentValue = currentValue,
                        GainLoss = gainLoss,
                        GainLossPercent = gainLossPercent
                    });

                    totalFinalValue += currentValue;
                }
            }
            else if (allocationStrategy == "weighted")
            {
                var totalConfidence = predictions.Sum(p => p.Confidence);

                foreach (var p in predictions)
                {
                    var weight = p.Confidence / totalConfidence;
                    var perTradeAmount = investmentAmount * weight;
                    var sharesPurchased = perTradeAmount / p.RecommendedPrice;
                    var currentValue = sharesPurchased * p.CurrentPrice;
                    var gainLoss = currentValue - perTradeAmount;
                    var gainLossPercent = perTradeAmount > 0 ? gainLoss / perTradeAmount : 0;

                    result.TradeDetails.Add(new WhatIfTradeDetail
                    {
                        Symbol = p.Symbol,
                        BarDate = p.BarDate,
                        InvestmentAmount = perTradeAmount,
                        RecommendedPrice = p.RecommendedPrice,
                        CurrentPrice = p.CurrentPrice,
                        SharesPurchased = sharesPurchased,
                        CurrentValue = currentValue,
                        GainLoss = gainLoss,
                        GainLossPercent = gainLossPercent
                    });

                    totalFinalValue += currentValue;
                }

                result.AverageInvestmentPerTrade = investmentAmount / predictions.Count;
            }

            result.FinalValue = totalFinalValue;
            whatIfResult = result;
            StateHasChanged();
        }

        private string GetPerformanceClass(decimal value)
        {
            return value > 0 ? "text-success" : value < 0 ? "text-danger" : "text-muted";
        }

        private string FormatPercent(decimal value)
        {
            var prefix = value >= 0 ? "+" : "";
            return prefix + value.ToString("P2");
        }

        private string FormatCurrency(decimal value)
        {
            var prefix = value >= 0 ? "+" : "";
            return prefix + value.ToString("C");
        }
    }
}
