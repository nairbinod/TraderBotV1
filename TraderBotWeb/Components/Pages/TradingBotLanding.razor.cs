using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using TraderBotWeb.Services;

namespace TraderBotWeb.Components.Pages
{
	/// <summary>
	/// Code-behind for TradingBotLanding.razor
	/// Moved from the inline @code block to keep markup and logic separate.
	/// </summary>
	public partial class TradingBotLanding : ComponentBase
	{
		// Use IDataService abstraction. By default page uses a mock service for demo.
		private readonly IDataService dataService = new SqlDataService();

		private List<RecommendationModel> recommendations = new();
		private SubscriptionModel Subscription = new();
		private bool SubscriptionSuccess = false;
		private TodaySummaryModel todaySummary = new();

		// Historic performance - master / detail
		private List<TradeHistoryModel> tradeHistory = new();
		private List<TradeHistoryDetailModel> tradeHistoryDetails = new();
		private string selectedSymbol = string.Empty;
		private DateTime historyFrom = DateTime.Today.AddDays(-30);
		private DateTime historyTo = DateTime.Today;
		private int selectedTradeHistoryId = 0;
		private bool isLoadingDetails = false;

		protected override async Task OnInitializedAsync()
		{
			// Load recommendations from the data service (mock by default)
			recommendations = (await dataService.GetRecommendationsAsync(1000)).ToList();

			todaySummary = new TodaySummaryModel
			{
				TotalSignals = recommendations.Count,
				AverageConfidence = recommendations.Count == 0 ? 0m : recommendations.Average(r => r.Confidence),
				HighQualityCount = recommendations.Count(r => r.Quality > .7m),
				MarketsScanned = recommendations.Select(r => r.Market).Distinct().Count(),
				SignalDate = recommendations.Count == 0 ? DateTime.Now : recommendations.Max(r => r.BarDate)
			};

			Subscription = new SubscriptionModel
			{
				Frequency = "Daily"
			};
		}

		private string GetQualityBadgeClass(string quality)
		{
			return quality switch
			{
				"High" => "badge bg-success-subtle text-success",
				"Medium" => "badge bg-warning-subtle text-warning",
				"Low" => "badge bg-secondary-subtle text-secondary",
				_ => "badge bg-light text-muted"
			};
		}

		private string GetPerformanceText(RecommendationModel rec)
		{
			if (rec.Price == 0) return "-";
			var perf = (rec.CurrentPrice - rec.Price) / rec.Price;
			return perf >= 0 ? $"+{perf.ToString("P1")}" : perf.ToString("P1");
		}

		private string GetPerformanceClass(RecommendationModel rec)
		{
			if (rec.Price == 0) return "text-muted";
			var perf = (rec.CurrentPrice - rec.Price) / rec.Price;
			return perf > 0 ? "text-success" : perf < 0 ? "text-danger" : "text-muted";
		}

		private async Task HandleSubscriptionAsync(EditContext context)
		{
			// Save subscriber via data service (mock or real)
			var _r = await dataService.SaveSubscriberAsync(Subscription);
			if (_r == 1) SubscriptionSuccess = true;
			StateHasChanged();
		}

		private async Task LoadTradeHistory()
		{
			// If no symbol selected, load all trades across symbols for the date range (master = all trades)
			//if (string.IsNullOrWhiteSpace(selectedSymbol))
			//{
				tradeHistory = (await dataService.GetAllTradeHistoryAsync(historyFrom, historyTo)).ToList();
				// clear details until user selects a master row
				tradeHistoryDetails.Clear();
				selectedTradeHistoryId = 0;
				StateHasChanged();
				return;
			//}

			//tradeHistory = (await dataService.GetTradeHistoryAsync(selectedSymbol, historyFrom, historyTo)).ToList();
			//// clear details until user selects a master row
			//tradeHistoryDetails.Clear();
			//selectedTradeHistoryId = 0;
			//StateHasChanged();
		}

		private async Task LoadTradeHistoryForSymbol(string symbol)
		{
			selectedSymbol = symbol;
			historyFrom = DateTime.Today.AddDays(-30);
			historyTo = DateTime.Today;
			await LoadTradeHistory();
		}

		private async Task OnSelectTradeHistory(int tradeHistoryId)
		{
			if (tradeHistoryId == selectedTradeHistoryId) return; // already selected

			selectedTradeHistoryId = tradeHistoryId;
			tradeHistoryDetails.Clear();
			isLoadingDetails = true;
			StateHasChanged();

			// Fetch details from TradeHistoryDetails table via data service
			var details = await dataService.GetTradeHistoryDetailsAsync(tradeHistoryId);
			tradeHistoryDetails = details.ToList();

			isLoadingDetails = false;
			StateHasChanged();
		}

		private class TodaySummaryModel
		{
			public int TotalSignals { get; set; }
			public decimal AverageConfidence { get; set; }
			public int HighQualityCount { get; set; }
			public int MarketsScanned { get; set; }
			public DateTime SignalDate { get; set; }
		}
	}
}
