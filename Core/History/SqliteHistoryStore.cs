namespace AiFuturesTerminal.Core.History;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _conn;

    public SqliteHistoryStore(string dbPath)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Trades (
    Symbol TEXT NOT NULL,
    TradeId INTEGER NOT NULL,
    OrderId INTEGER,
    Time INTEGER NOT NULL,
    Side TEXT,
    Qty TEXT,
    Price TEXT,
    QuoteQty TEXT,
    RealizedPnl TEXT,
    Commission TEXT,
    CommissionAsset TEXT,
    StrategyId TEXT,
    AccountEnv TEXT,
    PRIMARY KEY(Symbol, TradeId)
);

CREATE TABLE IF NOT EXISTS Orders (
    Symbol TEXT NOT NULL,
    OrderId INTEGER NOT NULL,
    CreateTime INTEGER NOT NULL,
    UpdateTime INTEGER,
    Status TEXT,
    Type TEXT,
    OrigQty TEXT,
    ExecutedQty TEXT,
    Price TEXT,
    AvgPrice TEXT,
    StrategyId TEXT,
    ClientOrderId TEXT,
    AccountEnv TEXT,
    PRIMARY KEY(Symbol, OrderId)
);

CREATE TABLE IF NOT EXISTS Meta (
    Key TEXT PRIMARY KEY,
    Value TEXT
);
";
        cmd.ExecuteNonQuery();
    }

    public async Task UpsertTradesAsync(IEnumerable<TradeHistoryRecord> trades, CancellationToken ct = default)
    {
        if (trades == null) return;
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR REPLACE INTO Trades (Symbol, TradeId, OrderId, Time, Side, Qty, Price, QuoteQty, RealizedPnl, Commission, CommissionAsset, StrategyId, AccountEnv)
VALUES ($s, $tid, $oid, $time, $side, $qty, $price, $quote, $rp, $fee, $feeAsset, $sid, $env);";

        var ps = new[] { "$s", "$tid", "$oid", "$time", "$side", "$qty", "$price", "$quote", "$rp", "$fee", "$feeAsset", "$sid", "$env" };
        foreach (var t in trades)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$s", t.Symbol ?? string.Empty);
            cmd.Parameters.AddWithValue("$tid", t.TradeId);
            cmd.Parameters.AddWithValue("$oid", t.OrderId);
            cmd.Parameters.AddWithValue("$time", t.Time.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$side", t.Side ?? string.Empty);
            cmd.Parameters.AddWithValue("$qty", t.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$price", t.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$quote", t.QuoteQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$rp", t.RealizedPnl.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$fee", t.Commission.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$feeAsset", t.CommissionAsset ?? string.Empty);
            cmd.Parameters.AddWithValue("$sid", t.StrategyId ?? string.Empty);
            cmd.Parameters.AddWithValue("$env", string.Empty);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        tx.Commit();
    }

    public async Task UpsertOrdersAsync(IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default)
    {
        if (orders == null) return;
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR REPLACE INTO Orders (Symbol, OrderId, CreateTime, UpdateTime, Status, Type, OrigQty, ExecutedQty, Price, AvgPrice, StrategyId, ClientOrderId, AccountEnv)
VALUES ($s, $oid, $create, $update, $status, $type, $orig, $exec, $price, $avg, $sid, $cid, $env);";

        foreach (var o in orders)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$s", o.Symbol ?? string.Empty);
            cmd.Parameters.AddWithValue("$oid", o.ExchangeOrderId);
            cmd.Parameters.AddWithValue("$create", o.CreateTime.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$update", o.UpdateTime.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$status", o.Status ?? string.Empty);
            cmd.Parameters.AddWithValue("$type", o.Type ?? string.Empty);
            cmd.Parameters.AddWithValue("$orig", o.OrigQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$exec", o.ExecutedQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$price", o.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$avg", o.AvgPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$sid", o.StrategyId ?? string.Empty);
            cmd.Parameters.AddWithValue("$cid", o.ClientOrderId ?? string.Empty);
            cmd.Parameters.AddWithValue("$env", string.Empty);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<TradeHistoryRecord>> QueryTradesAsync(HistoryQuery query, CancellationToken ct = default)
    {
        var list = new List<TradeHistoryRecord>();
        using var cmd = _conn.CreateCommand();
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT TradeId, OrderId, Symbol, Time, Side, Qty, Price, QuoteQty, RealizedPnl, Commission, CommissionAsset, StrategyId FROM Trades WHERE 1=1");
        if (!string.IsNullOrWhiteSpace(query.Symbol)) { sb.Append(" AND Symbol=$symbol"); cmd.Parameters.AddWithValue("$symbol", query.Symbol); }
        if (!string.IsNullOrWhiteSpace(query.RunId)) { sb.Append(" AND RunId=$runid"); cmd.Parameters.AddWithValue("$runid", query.RunId); }
        sb.Append(" AND Time >= $from AND Time <= $to");
        cmd.Parameters.AddWithValue("$from", query.From.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$to", query.To.ToUnixTimeMilliseconds());
        sb.Append(" ORDER BY Time ASC LIMIT $limit OFFSET $offset");
        cmd.Parameters.AddWithValue("$limit", query.PageSize);
        cmd.Parameters.AddWithValue("$offset", (query.Page - 1) * query.PageSize);
        cmd.CommandText = sb.ToString();

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var tradeId = reader.GetInt64(0);
            var orderId = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            var symbol = reader.GetString(2);
            var time = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)).UtcDateTime;
            var side = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var qty = decimal.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture);
            var price = decimal.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture);
            var quote = decimal.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture);
            var rp = decimal.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture);
            var fee = decimal.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture);
            var feeAsset = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
            var sid = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);

            list.Add(new TradeHistoryRecord
            {
                TradeId = tradeId,
                OrderId = orderId,
                Symbol = symbol,
                Side = side,
                PositionSide = string.Empty,
                Price = price,
                Qty = qty,
                QuoteQty = quote,
                RealizedPnl = rp,
                Commission = fee,
                CommissionAsset = feeAsset,
                Time = time,
                StrategyId = sid
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<OrderHistoryRecord>> QueryOrdersAsync(HistoryQuery query, CancellationToken ct = default)
    {
        var list = new List<OrderHistoryRecord>();
        using var cmd = _conn.CreateCommand();
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT OrderId, Symbol, CreateTime, UpdateTime, Status, Type, OrigQty, ExecutedQty, Price, AvgPrice, StrategyId, ClientOrderId FROM Orders WHERE 1=1");
        if (!string.IsNullOrWhiteSpace(query.Symbol)) { sb.Append(" AND Symbol=$symbol"); cmd.Parameters.AddWithValue("$symbol", query.Symbol); }
        if (!string.IsNullOrWhiteSpace(query.RunId)) { sb.Append(" AND RunId=$runid"); cmd.Parameters.AddWithValue("$runid", query.RunId); }
        sb.Append(" AND CreateTime >= $from AND CreateTime <= $to");
        cmd.Parameters.AddWithValue("$from", query.From.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$to", query.To.ToUnixTimeMilliseconds());
        sb.Append(" ORDER BY CreateTime ASC LIMIT $limit OFFSET $offset");
        cmd.Parameters.AddWithValue("$limit", query.PageSize);
        cmd.Parameters.AddWithValue("$offset", (query.Page - 1) * query.PageSize);
        cmd.CommandText = sb.ToString();

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var oid = reader.GetInt64(0);
            var symbol = reader.GetString(1);
            var create = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)).UtcDateTime;
            var update = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)).UtcDateTime;
            var status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var type = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var orig = decimal.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture);
            var exec = decimal.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture);
            var price = decimal.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture);
            var avg = decimal.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture);
            var sid = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
            var cid = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);

            list.Add(new OrderHistoryRecord
            {
                ExchangeOrderId = oid,
                Symbol = symbol,
                CreateTime = create,
                UpdateTime = update,
                Status = status,
                Type = type,
                OrigQty = orig,
                ExecutedQty = exec,
                Price = price,
                AvgPrice = avg,
                StrategyId = sid,
                ClientOrderId = cid
            });
        }

        return list;
    }

    public async Task<string?> GetMetaAsync(string key, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Meta WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key ?? string.Empty);
        var res = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return res == null || res == DBNull.Value ? null : (string)res;
    }

    public async Task SetMetaAsync(string key, string? value, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Meta (Key, Value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key ?? string.Empty);
        cmd.Parameters.AddWithValue("$v", value ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertTradesAndOrdersAsync(IEnumerable<TradeHistoryRecord> trades, IEnumerable<OrderHistoryRecord> orders, CancellationToken ct = default)
    {
        using var tx = _conn.BeginTransaction();

        if (trades != null)
        {
            using var tcmd = _conn.CreateCommand();
            tcmd.Transaction = tx;
            tcmd.CommandText = @"INSERT OR REPLACE INTO Trades (Symbol, TradeId, OrderId, Time, Side, Qty, Price, QuoteQty, RealizedPnl, Commission, CommissionAsset, StrategyId, AccountEnv)
VALUES ($s, $tid, $oid, $time, $side, $qty, $price, $quote, $rp, $fee, $feeAsset, $sid, $env);";

            foreach (var t in trades)
            {
                tcmd.Parameters.Clear();
                tcmd.Parameters.AddWithValue("$s", t.Symbol ?? string.Empty);
                tcmd.Parameters.AddWithValue("$tid", t.TradeId);
                tcmd.Parameters.AddWithValue("$oid", t.OrderId);
                tcmd.Parameters.AddWithValue("$time", t.Time.ToUnixTimeMilliseconds());
                tcmd.Parameters.AddWithValue("$side", t.Side ?? string.Empty);
                tcmd.Parameters.AddWithValue("$qty", t.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tcmd.Parameters.AddWithValue("$price", t.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tcmd.Parameters.AddWithValue("$quote", t.QuoteQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tcmd.Parameters.AddWithValue("$rp", t.RealizedPnl.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tcmd.Parameters.AddWithValue("$fee", t.Commission.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tcmd.Parameters.AddWithValue("$feeAsset", t.CommissionAsset ?? string.Empty);
                tcmd.Parameters.AddWithValue("$sid", t.StrategyId ?? string.Empty);
                tcmd.Parameters.AddWithValue("$env", string.Empty);
                await tcmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        if (orders != null)
        {
            using var ocmd = _conn.CreateCommand();
            ocmd.Transaction = tx;
            ocmd.CommandText = @"INSERT OR REPLACE INTO Orders (Symbol, OrderId, CreateTime, UpdateTime, Status, Type, OrigQty, ExecutedQty, Price, AvgPrice, StrategyId, ClientOrderId, AccountEnv)
VALUES ($s, $oid, $create, $update, $status, $type, $orig, $exec, $price, $avg, $sid, $cid, $env);";

            foreach (var o in orders)
            {
                ocmd.Parameters.Clear();
                ocmd.Parameters.AddWithValue("$s", o.Symbol ?? string.Empty);
                ocmd.Parameters.AddWithValue("$oid", o.ExchangeOrderId);
                ocmd.Parameters.AddWithValue("$create", o.CreateTime.ToUnixTimeMilliseconds());
                ocmd.Parameters.AddWithValue("$update", o.UpdateTime.ToUnixTimeMilliseconds());
                ocmd.Parameters.AddWithValue("$status", o.Status ?? string.Empty);
                ocmd.Parameters.AddWithValue("$type", o.Type ?? string.Empty);
                ocmd.Parameters.AddWithValue("$orig", o.OrigQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ocmd.Parameters.AddWithValue("$exec", o.ExecutedQty.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ocmd.Parameters.AddWithValue("$price", o.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ocmd.Parameters.AddWithValue("$avg", o.AvgPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ocmd.Parameters.AddWithValue("$sid", o.StrategyId ?? string.Empty);
                ocmd.Parameters.AddWithValue("$cid", o.ClientOrderId ?? string.Empty);
                ocmd.Parameters.AddWithValue("$env", string.Empty);
                await ocmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<(string Key, string Value)>> ListMetaAsync(string keyPrefix, int limit = 100, CancellationToken ct = default)
    {
        var list = new List<(string Key, string Value)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Meta WHERE Key LIKE $prefix || '%' ORDER BY Key DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$prefix", keyPrefix ?? string.Empty);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            list.Add((key, value));
        }

        return list;
    }

#if DEBUG
    /// <summary>
    /// Debug-only helper: deletes the history database contents to start fresh.
    /// Use only in development builds.
    /// </summary>
    public static void ClearDatabaseForDevelopment(string dbPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dbPath)) return;

            // Use a separate connection to issue DELETE statements; this works even if another connection is open.
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Trades;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM Orders;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM Meta;";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            // Run VACUUM to reclaim space
            using (var vacuum = conn.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
        }
        catch
        {
            // swallow - debug helper
        }
    }
#endif

    public void Dispose()
    {
        try { _conn?.Dispose(); } catch { }
    }
}
