using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraderBotV1
{

	// ═══════════════════════════════════════════════════════════════
	// Configuration Classes
	// ═══════════════════════════════════════════════════════════════
	public class Config
	{
		// Data Source Configuration
		public string DBPath { get; set; }
		public string? Mode { get; set; } = "Auto";
		public string? DataSource { get; set; } = "Auto";
		public int DaysHistory { get; set; } = 180;

		// Trading Configuration
		public decimal RiskPercent { get; set; } = 0.01m;
		public bool PersistPriceData { get; set; } = false;
		public bool UsePaperWhenLive { get; set; } = true;
		public bool EnableLiveTrading { get; set; } = false;

		// Email Notification Configuration
		public bool EnableEmailNotifications { get; set; } = false;
		public string? NotificationEmail { get; set; }

		// Symbols to Analyze
		public string[]? Symbols { get; set; }

		// API Configurations
		public AlpacaConfig Alpaca { get; set; } = new AlpacaConfig();
		public PolygonConfig Polygon { get; set; } = new PolygonConfig();
		public MailJetConfig? MailJet { get; set; }

		// Validation Helper
		public bool IsValid(out List<string> errors)
		{
			errors = new List<string>();

			if (DaysHistory <= 0)
				errors.Add("DaysHistory must be positive");

			if (RiskPercent <= 0 || RiskPercent > 0.5m)
				errors.Add("RiskPercent must be between 0 and 0.5 (0-50%)");

			if (EnableEmailNotifications)
			{
				if (string.IsNullOrWhiteSpace(NotificationEmail))
					errors.Add("NotificationEmail is required when EnableEmailNotifications is true");

				if (MailJet == null || string.IsNullOrWhiteSpace(MailJet.ApiKey) ||
					string.IsNullOrWhiteSpace(MailJet.ApiSecret))
					errors.Add("MailJet API credentials are required when EnableEmailNotifications is true");
			}

			var source = (DataSource ?? "Auto").ToLowerInvariant();
			var mode = (Mode ?? "Auto").ToLowerInvariant();

			bool needsPolygon = source == "polygon" || (source == "auto" && mode != "live");
			bool needsAlpaca = source == "alpaca" || (source == "auto" && mode == "live") || EnableLiveTrading;

			if (needsPolygon && string.IsNullOrWhiteSpace(Polygon?.ApiKey))
				errors.Add("Polygon API key is required for the selected configuration");

			if (needsAlpaca && (string.IsNullOrWhiteSpace(Alpaca?.ApiKey) ||
								string.IsNullOrWhiteSpace(Alpaca?.ApiSecret)))
				errors.Add("Alpaca API credentials are required for the selected configuration");

			return errors.Count == 0;
		}
	}

	public class AlpacaConfig
	{
		public string ApiKey { get; set; } = "";
		public string ApiSecret { get; set; } = "";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
									!string.IsNullOrWhiteSpace(ApiSecret);
	}

	public class PolygonConfig
	{
		public string ApiKey { get; set; } = "";
		public string Timespan { get; set; } = "day";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

		public bool IsValidTimespan()
		{
			var validTimespans = new[] { "minute", "hour", "day", "week", "month" };
			return validTimespans.Contains(Timespan?.ToLowerInvariant());
		}
	}

	public class MailJetConfig
	{
		public string ApiKey { get; set; } = "";
		public string ApiSecret { get; set; } = "";
		public string SenderEmail { get; set; } = "";
		public string? SenderName { get; set; } = "SmartBot Trading";

		public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
									!string.IsNullOrWhiteSpace(ApiSecret) &&
									!string.IsNullOrWhiteSpace(SenderEmail);

		public bool IsValidEmail(string email)
		{
			if (string.IsNullOrWhiteSpace(email))
				return false;

			try
			{
				var addr = new System.Net.Mail.MailAddress(email);
				return addr.Address == email;
			}
			catch
			{
				return false;
			}
		}

		public bool Validate(out List<string> errors)
		{
			errors = new List<string>();

			if (string.IsNullOrWhiteSpace(ApiKey))
				errors.Add("MailJet API Key is required");

			if (string.IsNullOrWhiteSpace(ApiSecret))
				errors.Add("MailJet API Secret is required");

			if (string.IsNullOrWhiteSpace(SenderEmail))
				errors.Add("MailJet Sender Email is required");
			else if (!IsValidEmail(SenderEmail))
				errors.Add($"Invalid sender email format: {SenderEmail}");

			return errors.Count == 0;
		}
	}


}
