using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
// Ensure that the Newtonsoft.Json package is installed in your project.
// You can install it via NuGet Package Manager in Visual Studio or by running the following command in the Package Manager Console:
// Install-Package Newtonsoft.Json

// Once the package is installed, the error CS0246 should be resolved as the namespace 'Newtonsoft.Json.Linq' will now be available.
using static System.Runtime.InteropServices.JavaScript.JSType;

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
			string recipientEmails,
			string subject,
			string htmlContent,
			string textContent = null)
		{
			try
			{
				var from = new
				{
					Email = _senderEmail,
					Name = _senderName
				};
				var toAddresses = recipientEmails
									.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
									.Select(e => e.Trim())
									.Where(e => e.Length > 0)
									.Distinct(StringComparer.OrdinalIgnoreCase)
									.Select(e => new Dictionary<string, string> { ["Email"] = e })
									.ToList();

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
									Email = _senderEmail
								}
							},
							Bcc = toAddresses,
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
					Console.WriteLine($"✅ Email sent successfully");
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

		private static List<string> ParseEmails(string emails)
		{
			if (string.IsNullOrWhiteSpace(emails)) return new List<string>();
			// split on comma or semicolon
			var list = emails
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			return list;
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
        .container { background-color: white; max-width: 1000px; margin: 0 auto; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; margin: -30px -30px 20px -30px; }
        h1 { margin: 0; font-size: 28px; }
        .timestamp { font-size: 14px; opacity: 0.9; margin-top: 5px; }
        .signal-table { width: 100%; border-collapse: collapse; margin: 20px 0; background-color: white; }
        .signal-table thead { background: linear-gradient(135deg, #28a745 0%, #20c997 100%); color: white; }
        .signal-table th { padding: 12px; text-align: left; font-weight: bold; font-size: 14px; }
        .signal-table td { padding: 12px; border-bottom: 1px solid #e0e0e0; }
        .signal-table tbody tr:hover { background-color: #f8f9fa; }
        .signal-table tbody tr:last-child td { border-bottom: none; }
        .symbol { font-weight: bold; color: #28a745; font-size: 16px; }
        .confidence { background-color: #28a745; color: white; padding: 4px 10px; border-radius: 15px; font-size: 12px; font-weight: bold; display: inline-block; }
        .footer { margin-top: 30px; padding-top: 20px; border-top: 2px solid #e0e0e0; text-align: center; color: #888; font-size: 12px; }
        .warning { background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin-top: 20px; border-radius: 5px; }
        .summary { background-color: #e7f3ff; padding: 15px; border-radius: 5px; margin-bottom: 20px; }
        .summary strong { color: #0066cc; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🚀 Buy Signals Detected</h1>
            <div class='timestamp'>" + DateTime.UtcNow.ToString("MMMM dd, yyyy HH:mm") + @" UTC</div>
        </div>
        <div class='summary'>
            <strong>Total Signals:</strong> " + signals.Count + @"
        </div>
        <table class='signal-table'>
            <thead>
                <tr>
                    <th>Symbol</th>
                    <th>Entry Price</th>
                    <th>Confidence</th>
                    <th>Stop Distance</th>
                    <th>Signal Date</th>
                    <th>Strategies</th>
                    <th>Reason</th>
                </tr>
            </thead>
            <tbody>");

			foreach (var signal in signals)
			{
				sb.AppendLine($@"
                <tr>
                    <td class='symbol'>{signal.Symbol}</td>
                    <td>${signal.EntryPrice:F2}</td>
                    <td><span class='confidence'>{signal.Confidence:P0}</span></td>
                    <td>${signal.StopDistance:F2}</td>
                    <td>{signal.LastBarDate:MM/dd/yyyy}</td>
                    <td>{signal.ConfirmedStrategies}</td>
                    <td>{signal.Reason}</td>
                </tr>");
			}

			sb.AppendLine(@"
            </tbody>
        </table>
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

			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("🚀 BUY SIGNALS DETECTED");
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine($"Date: {DateTime.UtcNow:MMMM dd, yyyy HH:mm} UTC");
			sb.AppendLine($"Total Signals: {signals.Count}");
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine();
			sb.AppendLine(string.Format("{0,-8} {1,10} {2,10} {3,12} {4,12} {5,4} {6}",
				"Symbol", "Entry", "Confidence", "Stop Dist", "Date", "Strat", "Reason"));
			sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");

			foreach (var signal in signals)
			{
				sb.AppendLine(string.Format("{0,-8} ${1,9:F2} {2,10:P0} ${3,11:F2} {4,12:MM/dd/yyyy} {5,4} {6}",
					signal.Symbol,
					signal.EntryPrice,
					signal.Confidence,
					signal.StopDistance,
					signal.LastBarDate,
					signal.ConfirmedStrategies,
					signal.Reason.Length > 30 ? signal.Reason.Substring(0, 27) + "..." : signal.Reason));
			}

			sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");
			sb.AppendLine();
			sb.AppendLine("⚠️ DISCLAIMER:");
			sb.AppendLine("This is an automated trading signal. Always verify signals manually before executing");
			sb.AppendLine("trades. Past performance does not guarantee future results.");
			sb.AppendLine();
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
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
		public DateTime LastBarDate { get; set; }
	}
}