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
	// Updated for new TradingSignal class structure
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
		/// Sends trading signal notifications for multiple symbols
		/// Updated to handle both Buy and Sell signals
		/// </summary>
		public async Task<bool> SendTradingSignalNotificationAsync(
			string recipientEmail,
			List<TradingSignal> signals)
		{
			if (signals == null || signals.Count == 0)
			{
				Console.WriteLine("⚠️ No signals to send");
				return false;
			}

			var buySignals = signals.Where(s => s.Direction == "Buy").ToList();
			var sellSignals = signals.Where(s => s.Direction == "Sell").ToList();

			if (buySignals.Count == 0 && sellSignals.Count == 0)
			{
				Console.WriteLine("⚠️ No buy or sell signals to send");
				return false;
			}

			try
			{
				string signalType = "";
				if (buySignals.Count > 0 && sellSignals.Count == 0)
					signalType = $"🚀 {buySignals.Count} Buy Signal{(buySignals.Count > 1 ? "s" : "")}";
				else if (sellSignals.Count > 0 && buySignals.Count == 0)
					signalType = $"🔻 {sellSignals.Count} Sell Signal{(sellSignals.Count > 1 ? "s" : "")}";
				else
					signalType = $"📊 {signals.Count} Trading Signal{(signals.Count > 1 ? "s" : "")} ({buySignals.Count} Buy, {sellSignals.Count} Sell)";

				var subject = $"{signalType} Detected - {DateTime.UtcNow:MMM dd, yyyy}";
				var htmlContent = GenerateTradingSignalHtml(signals, buySignals, sellSignals);
				var textContent = GenerateTradingSignalText(signals, buySignals, sellSignals);

				return await SendEmailAsync(recipientEmail, subject, htmlContent, textContent);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error sending notification: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Backward compatibility method for Buy signals only
		/// </summary>
		public async Task<bool> SendBuySignalNotificationAsync(
			string recipientEmail,
			List<TradingSignal> signals)
		{
			return await SendTradingSignalNotificationAsync(recipientEmail, signals);
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
					Console.WriteLine($"✅ Email sent successfully to {toAddresses.Count} recipient(s)");
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
		/// Generates HTML email content for trading signals
		/// Updated for new TradingSignal structure with Quality, Targets, MaxHoldDays
		/// </summary>
		private string GenerateTradingSignalHtml(List<TradingSignal> allSignals, List<TradingSignal> buySignals, List<TradingSignal> sellSignals)
		{
			var sb = new StringBuilder();

			// Determine header color based on signal type
			string headerGradient = buySignals.Count > 0 && sellSignals.Count == 0
				? "linear-gradient(135deg, #28a745 0%, #20c997 100%)"  // Green for buys
				: sellSignals.Count > 0 && buySignals.Count == 0
					? "linear-gradient(135deg, #dc3545 0%, #c82333 100%)"  // Red for sells
					: "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";  // Purple for mixed

			sb.AppendLine(@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }
        .container { background-color: white; max-width: 1200px; margin: 0 auto; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .header { background: " + headerGradient + @"; color: white; padding: 20px; border-radius: 10px 10px 0 0; margin: -30px -30px 20px -30px; }
        h1 { margin: 0; font-size: 28px; }
        .timestamp { font-size: 14px; opacity: 0.9; margin-top: 5px; }
        .summary { background-color: #e7f3ff; padding: 15px; border-radius: 5px; margin-bottom: 20px; display: flex; justify-content: space-around; }
        .summary-item { text-align: center; }
        .summary-item strong { display: block; font-size: 24px; color: #0066cc; }
        .summary-item span { font-size: 12px; color: #666; }
        .signal-section { margin-bottom: 30px; }
        .signal-section h2 { color: #333; border-bottom: 2px solid #e0e0e0; padding-bottom: 10px; margin-bottom: 15px; }
        .buy-section h2 { border-color: #28a745; color: #28a745; }
        .sell-section h2 { border-color: #dc3545; color: #dc3545; }
        .signal-table { width: 100%; border-collapse: collapse; margin: 20px 0; background-color: white; font-size: 13px; }
        .signal-table thead { background: #34495e; color: white; }
        .signal-table th { padding: 12px 8px; text-align: left; font-weight: bold; font-size: 12px; }
        .signal-table td { padding: 10px 8px; border-bottom: 1px solid #e0e0e0; vertical-align: top; }
        .signal-table tbody tr:hover { background-color: #f8f9fa; }
        .signal-table tbody tr:last-child td { border-bottom: none; }
        .symbol { font-weight: bold; font-size: 15px; }
        .symbol a { color: #0066cc; text-decoration: none; }
        .symbol a:hover { text-decoration: underline; }
        .buy-signal .symbol a { color: #28a745; }
        .sell-signal .symbol a { color: #dc3545; }
        .badge { color: white; padding: 3px 8px; border-radius: 12px; font-size: 11px; font-weight: bold; display: inline-block; white-space: nowrap; }
        .confidence { background-color: #667eea; }
        .quality { background-color: #ff6b6b; }
        .quality.high { background-color: #28a745; }
        .quality.medium { background-color: #ffc107; color: #333; }
        .quality.low { background-color: #dc3545; }
        .price { font-weight: bold; font-size: 14px; }
        .targets { font-size: 12px; color: #666; }
        .footer { margin-top: 30px; padding-top: 20px; border-top: 2px solid #e0e0e0; text-align: center; color: #888; font-size: 12px; }
        .warning { background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin-top: 20px; border-radius: 5px; }
        .info-box { background-color: #e3f2fd; border-left: 4px solid #2196F3; padding: 15px; margin: 15px 0; border-radius: 5px; }
        .metric { display: inline-block; margin-right: 15px; }
        .metric-label { font-size: 11px; color: #666; display: block; }
        .metric-value { font-size: 14px; font-weight: bold; display: block; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📊 Trading Signals Detected</h1>
            <div class='timestamp'>Signal Date: " + allSignals[0].SignalDate.ToString("MMMM dd, yyyy HH:mm") + @" UTC</div>
        </div>
        <div class='summary'>
            <div class='summary-item'>
                <strong>" + allSignals.Count + @"</strong>
                <span>Total Signals</span>
            </div>
            <div class='summary-item'>
                <strong style='color: #28a745;'>" + buySignals.Count + @"</strong>
                <span>Buy Signals</span>
            </div>
            <div class='summary-item'>
                <strong style='color: #dc3545;'>" + sellSignals.Count + @"</strong>
                <span>Sell Signals</span>
            </div>
            <div class='summary-item'>
                <strong>" + allSignals.Average(s => s.Confidence).ToString("P0") + @"</strong>
                <span>Avg Confidence</span>
            </div>
            <div class='summary-item'>
                <strong>" + allSignals.Average(s => s.Quality).ToString("P0") + @"</strong>
                <span>Avg Quality</span>
            </div>
        </div>");

			// Buy Signals Section
			if (buySignals.Count > 0)
			{
				sb.AppendLine(@"
        <div class='signal-section buy-section'>
            <h2>🚀 Buy Signals (" + buySignals.Count + @")</h2>
            <table class='signal-table'>
                <thead>
                    <tr>
                        <th>Symbol</th>
                        <th>Entry</th>
                        <th>Stop Loss</th>
                        <th>Target 1</th>
                        <th>Target 2</th>
                        <th>Conf/Qual</th>
                        <th>Max Hold</th>
                        <th>Reason</th>
                    </tr>
                </thead>
                <tbody>");

				foreach (var signal in buySignals.OrderByDescending(s => s.Quality))
				{
					string stockanalysisUrl = $"https://stockanalysis.com/stocks/{signal.Symbol}/";
					decimal stopPercent = ((signal.Entry - signal.StopLoss) / signal.Entry) * 100;
					decimal target1Percent = ((signal.Target1 - signal.Entry) / signal.Entry) * 100;
					decimal target2Percent = ((signal.Target2 - signal.Entry) / signal.Entry) * 100;

					string qualityClass = signal.Quality >= 0.65m ? "high" : signal.Quality >= 0.55m ? "medium" : "low";

					sb.AppendLine($@"
                    <tr class='buy-signal'>
                        <td class='symbol'><a href='{stockanalysisUrl}' target='_blank'>{signal.Symbol}</a></td>
                        <td class='price'>${signal.Entry:F2}</td>
                        <td>${signal.StopLoss:F2}<br><span class='targets'>-{stopPercent:F1}%</span></td>
                        <td>${signal.Target1:F2}<br><span class='targets'>+{target1Percent:F1}%</span></td>
                        <td>${signal.Target2:F2}<br><span class='targets'>+{target2Percent:F1}%</span></td>
                        <td>
                            <span class='badge confidence'>{signal.Confidence:P0}</span><br>
                            <span class='badge quality {qualityClass}'>{signal.Quality:P0}</span>
                        </td>
                        <td>{signal.MaxHoldDays} days</td>
                        <td style='font-size: 12px;'>{signal.Reason}</td>
                    </tr>");
				}

				sb.AppendLine(@"
                </tbody>
            </table>
        </div>");
			}

			// Sell Signals Section
			if (sellSignals.Count > 0)
			{
				sb.AppendLine(@"
        <div class='signal-section sell-section'>
            <h2>🔻 Sell Signals (" + sellSignals.Count + @")</h2>
            <table class='signal-table'>
                <thead>
                    <tr>
                        <th>Symbol</th>
                        <th>Entry</th>
                        <th>Stop Loss</th>
                        <th>Target 1</th>
                        <th>Target 2</th>
                        <th>Conf/Qual</th>
                        <th>Max Hold</th>
                        <th>Reason</th>
                    </tr>
                </thead>
                <tbody>");

				foreach (var signal in sellSignals.OrderByDescending(s => s.Quality))
				{
					string stockanalysisUrl = $"https://stockanalysis.com/stocks/{signal.Symbol}/";
					decimal stopPercent = ((signal.StopLoss - signal.Entry) / signal.Entry) * 100;
					decimal target1Percent = ((signal.Entry - signal.Target1) / signal.Entry) * 100;
					decimal target2Percent = ((signal.Entry - signal.Target2) / signal.Entry) * 100;

					string qualityClass = signal.Quality >= 0.65m ? "high" : signal.Quality >= 0.55m ? "medium" : "low";

					sb.AppendLine($@"
                    <tr class='sell-signal'>
                        <td class='symbol'><a href='{stockanalysisUrl}' target='_blank'>{signal.Symbol}</a></td>
                        <td class='price'>${signal.Entry:F2}</td>
                        <td>${signal.StopLoss:F2}<br><span class='targets'>+{stopPercent:F1}%</span></td>
                        <td>${signal.Target1:F2}<br><span class='targets'>+{target1Percent:F1}%</span></td>
                        <td>${signal.Target2:F2}<br><span class='targets'>+{target2Percent:F1}%</span></td>
                        <td>
                            <span class='badge confidence'>{signal.Confidence:P0}</span><br>
                            <span class='badge quality {qualityClass}'>{signal.Quality:P0}</span>
                        </td>
                        <td>{signal.MaxHoldDays} days</td>
                        <td style='font-size: 12px;'>{signal.Reason}</td>
                    </tr>");
				}

				sb.AppendLine(@"
                </tbody>
            </table>
        </div>");
			}

			sb.AppendLine(@"
        <div class='info-box'>
            <strong>📋 Legend:</strong><br>
            • <strong>Confidence:</strong> Strategy consensus strength (how many strategies agree)<br>
            • <strong>Quality:</strong> Overall setup quality score (trend, volume, volatility, momentum)<br>
            • <strong>Max Hold:</strong> Recommended maximum holding period before re-evaluation<br>
            • <strong>Stop Loss:</strong> Risk management exit level<br>
            • <strong>Target 1/2:</strong> Profit-taking levels (2.5:1 and 4:1 reward:risk ratios)
        </div>
        <div class='warning'>
            ⚠️ <strong>Risk Disclaimer:</strong> This is an automated trading signal generated by technical analysis. 
            Always verify signals manually before executing trades. Use proper position sizing (1-2% risk per trade). 
            Past performance does not guarantee future results. Never risk more than you can afford to lose.
        </div>
        <div class='footer'>
            Generated by SmartBot Trading System (Swing Trading Engine)<br>
            This is an automated notification. Please do not reply to this email.<br>
            <small>System: Ultra-Conservative / Balanced Optimized</small>
        </div>
    </div>
</body>
</html>");

			return sb.ToString();
		}

		/// <summary>
		/// Generates plain text email content for trading signals
		/// Updated for new TradingSignal structure
		/// </summary>
		private string GenerateTradingSignalText(List<TradingSignal> allSignals, List<TradingSignal> buySignals, List<TradingSignal> sellSignals)
		{
			var sb = new StringBuilder();

			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("📊 TRADING SIGNALS DETECTED");
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine($"Date: {DateTime.UtcNow:MMMM dd, yyyy HH:mm} UTC");
			sb.AppendLine($"Total Signals: {allSignals.Count} ({buySignals.Count} Buy, {sellSignals.Count} Sell)");
			sb.AppendLine($"Average Confidence: {allSignals.Average(s => s.Confidence):P0}");
			sb.AppendLine($"Average Quality: {allSignals.Average(s => s.Quality):P0}");
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine();

			// Buy Signals
			if (buySignals.Count > 0)
			{
				sb.AppendLine("🚀 BUY SIGNALS");
				sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");
				sb.AppendLine(string.Format("{0,-8} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}",
					"Symbol", "Entry", "Stop", "Target1", "Target2", "Conf", "Quality", "Hold"));
				sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");

				foreach (var signal in buySignals.OrderByDescending(s => s.Quality))
				{
					sb.AppendLine(string.Format("{0,-8} ${1,9:F2} ${2,9:F2} ${3,9:F2} ${4,9:F2} {5,8:P0} {6,8:P0} {7,4}d",
						signal.Symbol,
						signal.Entry,
						signal.StopLoss,
						signal.Target1,
						signal.Target2,
						signal.Confidence,
						signal.Quality,
						signal.MaxHoldDays));

					sb.AppendLine($"  Reason: {signal.Reason}");
					sb.AppendLine($"  Link: https://stockanalysis.com/stocks/{signal.Symbol}/");
					sb.AppendLine();
				}
			}

			// Sell Signals
			if (sellSignals.Count > 0)
			{
				sb.AppendLine("🔻 SELL SIGNALS");
				sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");
				sb.AppendLine(string.Format("{0,-8} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}",
					"Symbol", "Entry", "Stop", "Target1", "Target2", "Conf", "Quality", "Hold"));
				sb.AppendLine("───────────────────────────────────────────────────────────────────────────────────");

				foreach (var signal in sellSignals.OrderByDescending(s => s.Quality))
				{
					sb.AppendLine(string.Format("{0,-8} ${1,9:F2} ${2,9:F2} ${3,9:F2} ${4,9:F2} {5,8:P0} {6,8:P0} {7,4}d",
						signal.Symbol,
						signal.Entry,
						signal.StopLoss,
						signal.Target1,
						signal.Target2,
						signal.Confidence,
						signal.Quality,
						signal.MaxHoldDays));

					sb.AppendLine($"  Reason: {signal.Reason}");
					sb.AppendLine($"  Link: https://stockanalysis.com/stocks/{signal.Symbol}/");
					sb.AppendLine();
				}
			}

			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("LEGEND:");
			sb.AppendLine("  Confidence: Strategy consensus strength (how many strategies agree)");
			sb.AppendLine("  Quality: Overall setup quality (trend, volume, volatility, momentum)");
			sb.AppendLine("  Hold: Recommended maximum holding period in days");
			sb.AppendLine("  Stop: Risk management stop loss level");
			sb.AppendLine("  Target1/2: Profit targets (2.5:1 and 4:1 reward:risk ratios)");
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine();
			sb.AppendLine("⚠️ RISK DISCLAIMER:");
			sb.AppendLine("This is an automated trading signal generated by technical analysis. Always verify");
			sb.AppendLine("signals manually before executing trades. Use proper position sizing (1-2% risk per");
			sb.AppendLine("trade). Past performance does not guarantee future results. Never risk more than you");
			sb.AppendLine("can afford to lose.");
			sb.AppendLine();
			sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("Generated by SmartBot Trading System (Swing Trading Engine)");
			sb.AppendLine("System: Ultra-Conservative / Balanced Optimized");

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
}