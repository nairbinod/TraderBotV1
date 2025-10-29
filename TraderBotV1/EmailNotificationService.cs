using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TraderBotV1
{
	// ═══════════════════════════════════════════════════════════════
	// Email Notification Service using MailJet API
	// ═══════════════════════════════════════════════════════════════
	public class EmailNotificationService
	{
		private readonly string _apiKey;
		private readonly string _apiSecret;
		private readonly string _senderEmail;
		private readonly string _senderName;
		private readonly HttpClient _httpClient;

		private const string MAILJET_API_URL = "https://api.mailjet.com/v3.1/send";

		public EmailNotificationService(string apiKey, string apiSecret, string senderEmail, string senderName = "SmartBot Trading")
		{
			if (string.IsNullOrWhiteSpace(apiKey))
				throw new ArgumentNullException(nameof(apiKey));
			if (string.IsNullOrWhiteSpace(apiSecret))
				throw new ArgumentNullException(nameof(apiSecret));
			if (string.IsNullOrWhiteSpace(senderEmail))
				throw new ArgumentNullException(nameof(senderEmail));

			_apiKey = apiKey;
			_apiSecret = apiSecret;
			_senderEmail = senderEmail;
			_senderName = senderName;

			_httpClient = new HttpClient();

			// Set up Basic Authentication
			var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:{_apiSecret}"));
			_httpClient.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
		}

		/// <summary>
		/// Sends buy signal notifications for multiple symbols
		/// </summary>
		public async Task<bool> SendBuySignalNotificationAsync(
			string recipientEmail,
			List<TradingSignal> signals)
		{
			if (signals == null || signals.Count == 0)
			{
				Console.WriteLine("⚠️ No signals to send");
				return false;
			}

			var buySignals = signals.Where(s => s.Signal == "Buy").ToList();
			if (buySignals.Count == 0)
			{
				Console.WriteLine("⚠️ No buy signals to send");
				return false;
			}

			try
			{
				var subject = $"🚀 {buySignals.Count} Buy Signal{(buySignals.Count > 1 ? "s" : "")} Detected - {DateTime.UtcNow:MMM dd, yyyy}";
				var htmlContent = GenerateBuySignalHtml(buySignals);
				var textContent = GenerateBuySignalText(buySignals);

				return await SendEmailAsync(recipientEmail, subject, htmlContent, textContent);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error sending notification: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Sends a custom email notification
		/// </summary>
		public async Task<bool> SendEmailAsync(
			string recipientEmail,
			string subject,
			string htmlContent,
			string textContent = null)
		{
			try
			{
				var payload = new
				{
					Messages = new[]
					{
						new
						{
							From = new
							{
								Email = _senderEmail,
								Name = _senderName
							},
							To = new[]
							{
								new
								{
									Email = recipientEmail
								}
							},
							Subject = subject,
							TextPart = textContent ?? StripHtml(htmlContent),
							HTMLPart = htmlContent
						}
					}
				};

				var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
				{
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

				var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
				var response = await _httpClient.PostAsync(MAILJET_API_URL, content);

				if (response.IsSuccessStatusCode)
				{
					Console.WriteLine($"✅ Email sent successfully to {recipientEmail}");
					return true;
				}
				else
				{
					var errorBody = await response.Content.ReadAsStringAsync();
					Console.WriteLine($"❌ MailJet API error: {response.StatusCode}");
					Console.WriteLine($"   Response: {errorBody}");
					return false;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error sending email: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Generates HTML email content for buy signals
		/// </summary>
		private string GenerateBuySignalHtml(List<TradingSignal> signals)
		{
			var sb = new StringBuilder();

			sb.AppendLine(@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }
        .container { background-color: white; max-width: 800px; margin: 0 auto; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; margin: -30px -30px 20px -30px; }
        h1 { margin: 0; font-size: 28px; }
        .timestamp { font-size: 14px; opacity: 0.9; margin-top: 5px; }
        .signal-card { background-color: #f8f9fa; border-left: 4px solid #28a745; padding: 20px; margin-bottom: 20px; border-radius: 5px; }
        .symbol { font-size: 24px; font-weight: bold; color: #28a745; margin-bottom: 10px; }
        .confidence { display: inline-block; background-color: #28a745; color: white; padding: 5px 15px; border-radius: 20px; font-size: 14px; font-weight: bold; }
        .details { margin-top: 15px; }
        .detail-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #e0e0e0; }
        .detail-label { font-weight: bold; color: #555; }
        .detail-value { color: #333; }
        .footer { margin-top: 30px; padding-top: 20px; border-top: 2px solid #e0e0e0; text-align: center; color: #888; font-size: 12px; }
        .warning { background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin-top: 20px; border-radius: 5px; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🚀 Buy Signals Detected</h1>
            <div class='timestamp'>" + DateTime.UtcNow.ToString("MMMM dd, yyyy HH:mm") + @" UTC</div>
        </div>");

			foreach (var signal in signals)
			{
				sb.AppendLine($@"
        <div class='signal-card'>
            <div class='symbol'>{signal.Symbol}</div>
            <div class='confidence'>Confidence: {signal.Confidence:P0}</div>
            <div class='details'>
                <div class='detail-row'>
                    <span class='detail-label'>Entry Price:</span>
                    <span class='detail-value'>${signal.EntryPrice:F2}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-label'>Quantity:</span>
                    <span class='detail-value'>{signal.Quantity:N0} shares</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-label'>Position Value:</span>
                    <span class='detail-value'>${signal.EntryPrice * signal.Quantity:N2}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-label'>Stop Distance:</span>
                    <span class='detail-value'>${signal.StopDistance:F2}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-label'>Strategies Confirmed:</span>
                    <span class='detail-value'>{signal.ConfirmedStrategies}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-label'>Reason:</span>
                    <span class='detail-value'>{signal.Reason}</span>
                </div>
            </div>
        </div>");
			}

			sb.AppendLine(@"
        <div class='warning'>
            ⚠️ <strong>Disclaimer:</strong> This is an automated trading signal. Always verify signals manually before executing trades. Past performance does not guarantee future results.
        </div>
        <div class='footer'>
            Generated by SmartBot Trading System<br>
            This is an automated notification. Please do not reply to this email.
        </div>
    </div>
</body>
</html>");

			return sb.ToString();
		}

		/// <summary>
		/// Generates plain text email content for buy signals
		/// </summary>
		private string GenerateBuySignalText(List<TradingSignal> signals)
		{
			var sb = new StringBuilder();

			sb.AppendLine("═══════════════════════════════════════════════════════");
			sb.AppendLine("🚀 BUY SIGNALS DETECTED");
			sb.AppendLine("═══════════════════════════════════════════════════════");
			sb.AppendLine($"Date: {DateTime.UtcNow:MMMM dd, yyyy HH:mm} UTC");
			sb.AppendLine($"Total Signals: {signals.Count}");
			sb.AppendLine("═══════════════════════════════════════════════════════\n");

			foreach (var signal in signals)
			{
				sb.AppendLine($"Symbol: {signal.Symbol}");
				sb.AppendLine($"Confidence: {signal.Confidence:P0}");
				sb.AppendLine($"Entry Price: ${signal.EntryPrice:F2}");
				sb.AppendLine($"Quantity: {signal.Quantity:N0} shares");
				sb.AppendLine($"Position Value: ${signal.EntryPrice * signal.Quantity:N2}");
				sb.AppendLine($"Stop Distance: ${signal.StopDistance:F2}");
				sb.AppendLine($"Strategies Confirmed: {signal.ConfirmedStrategies}");
				sb.AppendLine($"Reason: {signal.Reason}");
				sb.AppendLine("───────────────────────────────────────────────────────\n");
			}

			sb.AppendLine("⚠️ DISCLAIMER:");
			sb.AppendLine("This is an automated trading signal. Always verify signals");
			sb.AppendLine("manually before executing trades. Past performance does not");
			sb.AppendLine("guarantee future results.");
			sb.AppendLine("\n═══════════════════════════════════════════════════════");
			sb.AppendLine("Generated by SmartBot Trading System");

			return sb.ToString();
		}

		/// <summary>
		/// Strips HTML tags from content (fallback for text version)
		/// </summary>
		private string StripHtml(string html)
		{
			return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty)
				.Replace("&nbsp;", " ")
				.Replace("&amp;", "&")
				.Replace("&lt;", "<")
				.Replace("&gt;", ">")
				.Trim();
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Trading Signal Data Model
	// ═══════════════════════════════════════════════════════════════
	public class TradingSignal
	{
		public string Symbol { get; set; } = "";
		public string Signal { get; set; } = "Hold";
		public decimal Confidence { get; set; }
		public decimal EntryPrice { get; set; }
		public decimal Quantity { get; set; }
		public decimal StopDistance { get; set; }
		public int ConfirmedStrategies { get; set; }
		public string Reason { get; set; } = "";
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	}
}