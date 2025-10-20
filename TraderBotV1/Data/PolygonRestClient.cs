using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TraderBotV1.Data
{
	public class PolygonBar
	{
		public long t { get; set; }
		public decimal o { get; set; }
		public decimal h { get; set; }
		public decimal l { get; set; }
		public decimal c { get; set; }
		public long v { get; set; }
	}

	public class PolygonResponse
	{
		public string ticker { get; set; } = string.Empty;
		public string status { get; set; } = string.Empty;
		public List<PolygonBar> results { get; set; } = new();
	}

	public class PolygonRestClient
	{
		private readonly HttpClient _http;
		private readonly string _apiKey;

		public PolygonRestClient(string apiKey)
		{
			_apiKey = apiKey;
			_http = new HttpClient { BaseAddress = new Uri("https://api.polygon.io/") };
		}

		public async Task<List<PolygonBar>> GetBarsAsync(string symbol, string timespan = "minute", int days = 5, int limit = 50000)
		{
			var to = DateTime.UtcNow;
			var from = to.AddDays(-days);

			string url = $"v2/aggs/ticker/{symbol}/range/1/{timespan}/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}?adjusted=true&sort=asc&limit={limit}&apiKey={_apiKey}";

			var response = await _http.GetAsync(url);
			response.EnsureSuccessStatusCode();

			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var parsed = await response.Content.ReadFromJsonAsync<PolygonResponse>(options);

			return parsed?.results ?? new List<PolygonBar>();
		}
	}
}
