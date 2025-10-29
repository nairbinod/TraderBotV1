using Alpaca.Markets;
using SQLitePCL;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraderBotV1.Data;

namespace TraderBotV1
{
	class Program
	{
		private const string CONFIG_FILE = "appsettings.json";

		static async Task Main()
		{
			try
			{
				// Initialize SQLite
				Batteries.Init();

				Console.WriteLine("═══════════════════════════════════════════════════════");
				Console.WriteLine("🚀 SmartBot Trading System");
				Console.WriteLine("═══════════════════════════════════════════════════════\n");

				// Load configuration
				var cfg = LoadConfiguration();
				ValidateConfiguration(cfg);

				// Display configuration
				PrintConfiguration(cfg);

				// Initialize database
				var db = InitializeDatabase();

				// Build market data provider (Polygon or Alpaca)
				var dataProvider = DataProviderFactory.Create(cfg);

				// Initialize email notification service (if configured)
				EmailNotificationService? emailService = null;
				if (cfg.EnableEmailNotifications && cfg.MailJet != null)
				{
					emailService = InitializeEmailService(cfg);
				}

				// Optional: Initialize trading client (if live trading enabled)
				IAlpacaTradingClient? tradingClient = null;
				if (cfg.EnableLiveTrading)
				{
					tradingClient = InitializeTradingClient(cfg);
				}

				// Run the bot
				var bot = new SmartBot(dataProvider, db, cfg, emailService);
				await bot.RunAsync();

				Console.WriteLine("\n✅ SmartBot execution completed successfully.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"\n❌ Fatal error: {ex.Message}");
				Console.WriteLine($"Stack trace: {ex.StackTrace}");
				Environment.Exit(1);
			}
		}

		private static Config LoadConfiguration()
		{
			string configPath = Path.Combine(Environment.CurrentDirectory, CONFIG_FILE);

			if (!File.Exists(configPath))
			{
				Console.WriteLine($"⚠️ Config file not found: {configPath}");
				Console.WriteLine("📝 Creating default configuration...");
				CreateDefaultConfig(configPath);
			}

			try
			{
				Console.WriteLine($"📄 Loading configuration from: {configPath}");
				string json = File.ReadAllText(configPath);

				var options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					Converters = { new JsonStringEnumConverter() },
					ReadCommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true
				};

				var config = JsonSerializer.Deserialize<Config>(json, options);

				if (config == null)
					throw new InvalidOperationException("Failed to deserialize configuration");

				Console.WriteLine("✅ Configuration loaded successfully\n");
				return config;
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException($"Invalid JSON in config file: {ex.Message}", ex);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Error reading config file: {ex.Message}", ex);
			}
		}

		private static void ValidateConfiguration(Config cfg)
		{
			Console.WriteLine("🔍 Validating configuration...");

			// Use the built-in validation method
			if (!cfg.IsValid(out List<string> errors))
			{
				Console.WriteLine("❌ Configuration validation failed:\n");
				foreach (var error in errors)
				{
					Console.WriteLine($"   • {error}");
				}
				throw new InvalidOperationException($"Configuration has {errors.Count} error(s). Please fix the issues above.");
			}

			// Additional validation for Polygon timespan
			if (cfg.Polygon?.IsConfigured == true && !cfg.Polygon.IsValidTimespan())
			{
				throw new InvalidOperationException(
					$"Invalid Polygon timespan: '{cfg.Polygon.Timespan}'. " +
					"Valid values: minute, hour, day, week, month");
			}

			// Validate MailJet configuration if email notifications are enabled
			if (cfg.EnableEmailNotifications && cfg.MailJet != null)
			{
				if (!cfg.MailJet.Validate(out List<string> mailjetErrors))
				{
					Console.WriteLine("❌ MailJet configuration errors:\n");
					foreach (var error in mailjetErrors)
					{
						Console.WriteLine($"   • {error}");
					}
					throw new InvalidOperationException("MailJet configuration is invalid");
				}

				// Validate notification email format
				if (!string.IsNullOrWhiteSpace(cfg.NotificationEmail) &&
					!cfg.MailJet.IsValidEmail(cfg.NotificationEmail))
				{
					throw new InvalidOperationException($"Invalid notification email format: {cfg.NotificationEmail}");
				}
			}

			Console.WriteLine("✅ Configuration validated\n");
		}

		private static void PrintConfiguration(Config cfg)
		{
			Console.WriteLine("⚙️  Configuration:");
			Console.WriteLine($"   Mode:               {cfg.Mode ?? "Auto"}");
			Console.WriteLine($"   Data Source:        {cfg.DataSource ?? "Auto"}");
			Console.WriteLine($"   Days History:       {cfg.DaysHistory}");
			Console.WriteLine($"   Risk Per Trade:     {cfg.RiskPercent:P2}");
			Console.WriteLine($"   Persist Price Data:  {cfg.PersistPriceData}");
			Console.WriteLine($"   Paper Trading:      {cfg.UsePaperWhenLive}");
			Console.WriteLine($"   Live Trading:       {cfg.EnableLiveTrading}");
			Console.WriteLine($"   Email Notifications: {cfg.EnableEmailNotifications}");

			if (cfg.EnableEmailNotifications && !string.IsNullOrWhiteSpace(cfg.NotificationEmail))
				Console.WriteLine($"   Notification Email:  {cfg.NotificationEmail}");

			if (cfg.Symbols != null && cfg.Symbols.Length > 0)
				Console.WriteLine($"   Symbols:            {string.Join(", ", cfg.Symbols)}");
			else
				Console.WriteLine($"   Symbols:            (will load from database)");

			Console.WriteLine();
		}

		private static EmailNotificationService InitializeEmailService(Config cfg)
		{
			try
			{
				Console.WriteLine("📧 Initializing email notification service...");

				if (string.IsNullOrWhiteSpace(cfg.MailJet?.ApiKey) ||
					string.IsNullOrWhiteSpace(cfg.MailJet?.ApiSecret))
				{
					throw new InvalidOperationException("MailJet API credentials are missing");
				}

				if (string.IsNullOrWhiteSpace(cfg.MailJet?.SenderEmail))
				{
					throw new InvalidOperationException("MailJet sender email is missing");
				}

				var service = new EmailNotificationService(
					cfg.MailJet.ApiKey,
					cfg.MailJet.ApiSecret,
					cfg.MailJet.SenderEmail,
					cfg.MailJet.SenderName ?? "SmartBot Trading"
				);

				Console.WriteLine($"✅ Email service initialized (from: {cfg.MailJet.SenderEmail})");
				Console.WriteLine();
				return service;
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to initialize email service: {ex.Message}", ex);
			}
		}

		private static SqliteStorage InitializeDatabase()
		{
			try
			{
				Console.WriteLine("💾 Initializing database...");
				var db = new SqliteStorage();
				Console.WriteLine("✅ Database ready\n");
				return db;
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to initialize database: {ex.Message}", ex);
			}
		}

		private static IAlpacaTradingClient InitializeTradingClient(Config cfg)
		{
			try
			{
				Console.WriteLine("📈 Initializing trading client...");

				var env = cfg.UsePaperWhenLive ? Environments.Paper : Environments.Live;
				var client = env.GetAlpacaTradingClient(
					new SecretKey(cfg.Alpaca.ApiKey, cfg.Alpaca.ApiSecret)
				);

				Console.WriteLine($"✅ Trading client initialized ({(cfg.UsePaperWhenLive ? "PAPER" : "LIVE")} mode)");

				if (!cfg.UsePaperWhenLive)
				{
					Console.WriteLine("⚠️  WARNING: LIVE TRADING MODE ENABLED!");
					Console.WriteLine("⚠️  Real money will be used for trades!");
				}

				Console.WriteLine();
				return client;
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to initialize trading client: {ex.Message}", ex);
			}
		}

		private static void CreateDefaultConfig(string configPath)
		{
			var defaultConfig = new Config
			{
				Mode = "Auto",
				DataSource = "Auto",
				DaysHistory = 180,
				RiskPercent = 0.01m,
				PersistPriceData = false,
				UsePaperWhenLive = true,
				EnableLiveTrading = false,
				EnableEmailNotifications = false,
				NotificationEmail = "your-email@example.com",
				Symbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "TSLA" },
				Alpaca = new AlpacaConfig
				{
					ApiKey = "YOUR_ALPACA_API_KEY",
					ApiSecret = "YOUR_ALPACA_API_SECRET"
				},
				Polygon = new PolygonConfig
				{
					ApiKey = "YOUR_POLYGON_API_KEY",
					Timespan = "day"
				},
				MailJet = new MailJetConfig
				{
					ApiKey = "YOUR_MAILJET_API_KEY",
					ApiSecret = "YOUR_MAILJET_API_SECRET",
					SenderEmail = "noreply@yourdomain.com",
					SenderName = "SmartBot Trading"
				}
			};

			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				Converters = { new JsonStringEnumConverter() }
			};

			string json = JsonSerializer.Serialize(defaultConfig, options);
			File.WriteAllText(configPath, json);

			Console.WriteLine($"✅ Created default config at: {configPath}");
			Console.WriteLine("⚠️  Please update the API keys before running again.");
			Environment.Exit(0);
		}
	}
}