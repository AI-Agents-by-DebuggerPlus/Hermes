using Hermes.TradingPlatform.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Hermes.TradingPlatform.Data.Persistence.Sql;

/// <summary>
/// SQLite-backed alternative to <see cref="TradeJournalFileWriter"/>. Stores trade journal
/// entries in <c>%LocalAppData%/HermesTrading/trade_journal.db</c>. Schema migrations are
/// idempotent (CREATE TABLE IF NOT EXISTS) so the file is forward-compatible.
/// </summary>
public sealed class SqliteJournalStore : IJournalStore
{
    private readonly object _sync = new();
    private readonly string _connectionString;

    public SqliteJournalStore(string? databasePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);

        DatabasePath = databasePath ?? Path.Combine(dir, "trade_journal.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        EnsureSchema();
    }

    public string DatabasePath { get; }

    public string Location => DatabasePath;

    public void Append(TradeJournalEntry entry)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO journal_entries
                    (id, ts_utc, order_id, symbol, kind, side, quantity, fill_price, fee,
                     realized_pnl, balance_before, balance_after, reduce_only)
                VALUES
                    ($id, $ts, $orderId, $symbol, $kind, $side, $qty, $price, $fee,
                     $pnl, $balBefore, $balAfter, $ro);
                """;

            cmd.Parameters.AddWithValue("$id", entry.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$ts", entry.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$orderId", entry.OrderId ?? string.Empty);
            cmd.Parameters.AddWithValue("$symbol", entry.Symbol ?? string.Empty);
            cmd.Parameters.AddWithValue("$kind", entry.Kind ?? string.Empty);
            cmd.Parameters.AddWithValue("$side", entry.Side ?? string.Empty);
            cmd.Parameters.AddWithValue("$qty", (double)entry.Quantity);
            cmd.Parameters.AddWithValue("$price", (double)entry.FillPrice);
            cmd.Parameters.AddWithValue("$fee", (double)entry.Fee);
            cmd.Parameters.AddWithValue("$pnl", (double)entry.RealizedPnl);
            cmd.Parameters.AddWithValue("$balBefore", (double)entry.BalanceBefore);
            cmd.Parameters.AddWithValue("$balAfter", (double)entry.BalanceAfter);
            cmd.Parameters.AddWithValue("$ro", entry.ReduceOnly ? 1 : 0);

            cmd.ExecuteNonQuery();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM journal_entries;";
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TradeJournalEntry> LoadAll()
    {
        var list = new List<TradeJournalEntry>();
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT id, ts_utc, order_id, symbol, kind, side, quantity, fill_price, fee,
                       realized_pnl, balance_before, balance_after, reduce_only
                FROM journal_entries
                ORDER BY ts_utc ASC, rowid ASC;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TradeJournalEntry
                {
                    Id = Guid.TryParseExact(reader.GetString(0), "N", out var gid) ? gid : Guid.NewGuid(),
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    OrderId = reader.GetString(2),
                    Symbol = reader.GetString(3),
                    Kind = reader.GetString(4),
                    Side = reader.GetString(5),
                    Quantity = (decimal)reader.GetDouble(6),
                    FillPrice = (decimal)reader.GetDouble(7),
                    Fee = (decimal)reader.GetDouble(8),
                    RealizedPnl = (decimal)reader.GetDouble(9),
                    BalanceBefore = (decimal)reader.GetDouble(10),
                    BalanceAfter = (decimal)reader.GetDouble(11),
                    ReduceOnly = reader.GetInt32(12) != 0,
                });
            }
        }

        return list;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                """;
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    private void EnsureSchema()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS journal_entries (
                    id              TEXT PRIMARY KEY,
                    ts_utc          INTEGER NOT NULL,
                    order_id        TEXT NOT NULL,
                    symbol          TEXT NOT NULL,
                    kind            TEXT NOT NULL,
                    side            TEXT NOT NULL,
                    quantity        REAL NOT NULL,
                    fill_price      REAL NOT NULL,
                    fee             REAL NOT NULL,
                    realized_pnl    REAL NOT NULL,
                    balance_before  REAL NOT NULL,
                    balance_after   REAL NOT NULL,
                    reduce_only     INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_journal_entries_ts ON journal_entries(ts_utc);
                CREATE INDEX IF NOT EXISTS ix_journal_entries_symbol ON journal_entries(symbol);
                """;
            cmd.ExecuteNonQuery();
        }
    }
}
