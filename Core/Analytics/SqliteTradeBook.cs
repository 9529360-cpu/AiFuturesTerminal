using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using AiFuturesTerminal.Core.Models;

namespace AiFuturesTerminal.Core.Analytics
{
    public sealed class SqliteTradeBook : ITradeBook, IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connString;
        private readonly object _sync = new();

        public event EventHandler<TradeRecord>? TradeRecorded;

        public SqliteTradeBook(string? path = null)
        {
            _dbPath = path ?? Path.Combine(AppContext.BaseDirectory, "trades.db");
            _connString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            Initialize();
        }

        private void Initialize()
        {
            lock (_sync)
            {
                var created = !File.Exists(_dbPath);

                using var conn = new SqliteConnection(_connString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS trades (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OpenTime TEXT NOT NULL,
    CloseTime TEXT NOT NULL,
    Symbol TEXT NOT NULL,
    StrategyName TEXT,
    Side INTEGER,
    Quantity REAL,
    EntryPrice REAL,
    ExitPrice REAL,
    RealizedPnl REAL,
    Fee REAL,
    Mode INTEGER,
    SessionId TEXT
);
";
                cmd.ExecuteNonQuery();
            }
        }

        public void AddTrade(TradeRecord trade)
        {
            // synchronous wrapper for compatibility
            AddAsync(trade).GetAwaiter().GetResult();
        }

        public async Task AddAsync(TradeRecord trade, CancellationToken ct = default)
        {
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO trades (OpenTime, CloseTime, Symbol, StrategyName, Side, Quantity, EntryPrice, ExitPrice, RealizedPnl, Fee, Mode, SessionId)
VALUES ($open, $close, $sym, $strat, $side, $qty, $entry, $exit, $pnl, $fee, $mode, $sid);";

            cmd.Parameters.AddWithValue("$open", trade.OpenTime.ToString("o"));
            cmd.Parameters.AddWithValue("$close", trade.CloseTime.ToString("o"));
            cmd.Parameters.AddWithValue("$sym", trade.Symbol);
            cmd.Parameters.AddWithValue("$strat", trade.StrategyName ?? string.Empty);
            cmd.Parameters.AddWithValue("$side", (int)trade.Side);
            cmd.Parameters.AddWithValue("$qty", (double)trade.Quantity);
            cmd.Parameters.AddWithValue("$entry", (double)trade.EntryPrice);
            cmd.Parameters.AddWithValue("$exit", (double)trade.ExitPrice);
            cmd.Parameters.AddWithValue("$pnl", (double)trade.RealizedPnl);
            cmd.Parameters.AddWithValue("$fee", (double)trade.Fee);
            cmd.Parameters.AddWithValue("$mode", (int)trade.Mode);
            cmd.Parameters.AddWithValue("$sid", Guid.NewGuid().ToString());

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // raise event on thread pool to avoid blocking caller
            _ = Task.Run(() => TradeRecorded?.Invoke(this, trade));
        }

        public async Task<IReadOnlyList<TradeRecord>> GetTradesAsync(DateTime? from = null, DateTime? to = null, string? strategyName = null, CancellationToken ct = default)
        {
            var list = new List<TradeRecord>();
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            using var cmd = conn.CreateCommand();
            var where = new List<string>();
            if (from.HasValue) { where.Add("CloseTime >= $from"); cmd.Parameters.AddWithValue("$from", from.Value.ToString("o")); }
            if (to.HasValue) { where.Add("CloseTime <= $to"); cmd.Parameters.AddWithValue("$to", to.Value.ToString("o")); }
            if (!string.IsNullOrEmpty(strategyName)) { where.Add("StrategyName = $strat"); cmd.Parameters.AddWithValue("$strat", strategyName); }

            var sql = "SELECT OpenTime, CloseTime, Symbol, StrategyName, Side, Quantity, EntryPrice, ExitPrice, RealizedPnl, Fee, Mode FROM trades";
            if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
            sql += " ORDER BY CloseTime ASC";
            cmd.CommandText = sql;

            using var rdr = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rdr.ReadAsync(ct).ConfigureAwait(false))
            {
                var open = DateTime.Parse(rdr.GetString(0));
                var close = DateTime.Parse(rdr.GetString(1));
                var symbol = rdr.GetString(2);
                var strat = rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3);
                var side = (AiFuturesTerminal.Core.Analytics.TradeSide)rdr.GetInt32(4);
                var qty = (decimal)rdr.GetDouble(5);
                var entry = (decimal)rdr.GetDouble(6);
                var exit = (decimal)rdr.GetDouble(7);
                var pnl = (decimal)rdr.GetDouble(8);
                var fee = (decimal)rdr.GetDouble(9);
                var mode = (AiFuturesTerminal.Core.Execution.ExecutionMode)rdr.GetInt32(10);

                list.Add(new TradeRecord
                {
                    OpenTime = open,
                    CloseTime = close,
                    Symbol = symbol,
                    StrategyName = strat,
                    Side = side,
                    Quantity = qty,
                    EntryPrice = entry,
                    ExitPrice = exit,
                    RealizedPnl = pnl,
                    Fee = fee,
                    Mode = mode
                });
            }

            return list;
        }

        public IReadOnlyList<TradeRecord> GetTrades(DateOnly date)
        {
            var from = date.ToDateTime(TimeOnly.MinValue);
            var to = date.ToDateTime(TimeOnly.MaxValue);
            return GetTradesAsync(from, to).GetAwaiter().GetResult();
        }

        public DailyTradeSummary GetDailySummary(DateOnly date)
        {
            var trades = GetTrades(date);
            var totalPnL = trades.Sum(t => t.RealizedPnl);
            var winCount = trades.Count(t => t.RealizedPnl > 0);
            var loseCount = trades.Count(t => t.RealizedPnl < 0);

            decimal running = 0m;
            decimal minEquity = 0m;
            foreach (var t in trades.OrderBy(t => t.CloseTime))
            {
                running += t.RealizedPnl;
                if (running < minEquity) minEquity = running;
            }

            return new DailyTradeSummary
            {
                TradingDate = date,
                TradeCount = trades.Count,
                WinCount = winCount,
                LoseCount = loseCount,
                TotalPnL = totalPnL,
                MaxDrawdown = minEquity
            };
        }

        public IReadOnlyList<TradeRecord> GetAllTrades()
        {
            return GetTradesAsync(null, null).GetAwaiter().GetResult();
        }

        public async Task<IReadOnlyList<TradeRecord>> GetAllTradesAsync(CancellationToken ct = default)
        {
            return await GetTradesAsync(null, null, null, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            // nothing to dispose currently
        }
    }
}
