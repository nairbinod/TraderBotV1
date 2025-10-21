using Alpaca.Markets;
using SQLitePCL;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraderBotV1.Data;

namespace TraderBotV1
{
    class Program
    {
        static async Task Main()
        {
            Batteries.Init();
            Console.WriteLine("🚀 SmartBot (Hybrid Data) starting...");

            //Read configuration from appsettings.json
            var cfg = LoadConfigFromFile(Path.Combine(Environment.CurrentDirectory, "appsettings.json"));

            // Trading client (paper/live handled by config, but we instantiate paper here for safety)
            var tradingClient = Environments.Paper.GetAlpacaTradingClient(
                new SecretKey(cfg.Alpaca.ApiKey, cfg.Alpaca.ApiSecret));

            // Build market data provider (Polygon or Alpaca) via factory
            var provider = DataProviderFactory.Create(cfg);

            var db = new SqliteStorage();
            var bot = new SmartBot(provider, db, cfg);
            await bot.RunAsync();

            Console.WriteLine("✅ Done.");
        }

        public static Config LoadConfigFromFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() },
                    ReadCommentHandling = JsonCommentHandling.Skip // Ignore comments in JSON
                };

                return JsonSerializer.Deserialize<Config>(json, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config file: addict");
                throw;
            }
        }

    }
}