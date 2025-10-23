using Microsoft.Data.Sqlite;

namespace TraderBotV1.Data
{
    public class SqliteStorage
    {
        private readonly string _conn;

        public SqliteStorage()
        {
            string path = Path.Combine(@"C:\github\nairbinod\TraderBotV1\TraderBotV1\Data", "TraderBot.v1.db");
            if (!File.Exists(path))
            {
                Console.WriteLine($"🆕 Creating new SQLite database at {path}...");
                using (File.Create(path)) { }
            }
            _conn = $"Data Source={path}";
            Initialize();
        }

        private void Initialize()
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
				CREATE TABLE IF NOT EXISTS Prices (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Symbol TEXT, Timestamp TEXT,
					Open REAL, High REAL, Low REAL, Close REAL, Volume REAL
				);
				CREATE TABLE IF NOT EXISTS Signals (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Symbol TEXT, Timestamp TEXT,
					Strategy TEXT, Signal TEXT, Meta TEXT
				);
				CREATE TABLE IF NOT EXISTS Trades (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Symbol TEXT, Timestamp TEXT,
					Side TEXT, Quantity INTEGER, Price REAL
				);
                DELETE from Prices;DELETE from Signals;INSERT INTO TradesHistory SELECT * FROM Trades; DELETE from Trades;";
            cmd.ExecuteNonQuery();
        }

        public void InsertPrice(string s, DateTime ts, decimal o, decimal h, decimal l, decimal c, long v)
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Prices (Symbol,Timestamp,Open,High,Low,Close,Volume) VALUES ($s,$t,$o,$h,$l,$c,$v)";
            cmd.Parameters.AddWithValue("$s", s);
            cmd.Parameters.AddWithValue("$t", ts.ToString("O"));
            cmd.Parameters.AddWithValue("$o", o);
            cmd.Parameters.AddWithValue("$h", h);
            cmd.Parameters.AddWithValue("$l", l);
            cmd.Parameters.AddWithValue("$c", c);
            cmd.Parameters.AddWithValue("$v", v);
            cmd.ExecuteNonQuery();
        }

        public void InsertSignal(string s, DateTime ts, string strategy, string signal, string meta)
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Signals (Symbol,Timestamp,Strategy,Signal,Meta) VALUES ($s,$t,$st,$si,$m)";
            cmd.Parameters.AddWithValue("$s", s);
            cmd.Parameters.AddWithValue("$t", ts.ToString("O"));
            cmd.Parameters.AddWithValue("$st", strategy);
            cmd.Parameters.AddWithValue("$si", signal);
            cmd.Parameters.AddWithValue("$m", meta);
            cmd.ExecuteNonQuery();
        }

        public void InsertTrade(string s, DateTime ts, string side, long qty, decimal price)
        {
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Trades (Symbol,Timestamp,Side,Quantity,Price) VALUES ($s,$t,$side,$q,$p)";
            cmd.Parameters.AddWithValue("$s", s);
            cmd.Parameters.AddWithValue("$t", ts.ToString("O"));
            cmd.Parameters.AddWithValue("$side", side);
            cmd.Parameters.AddWithValue("$q", qty);
            cmd.Parameters.AddWithValue("$p", price);
            cmd.ExecuteNonQuery();
        }

        public List<string> GetActiveSymbols()
        {
            var symbols = new List<string>();
            using var conn = new SqliteConnection(_conn);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Symbol FROM Symbols WHERE Active = 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                symbols.Add(reader.GetString(0));
            }
            return symbols;
        }

    }
}
