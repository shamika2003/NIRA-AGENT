using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NIRAAgent.Browser;

// Durable, metadata-only write-ahead receipts. An in-flight receipt survives a
// process crash as Dispatching: there is no automatic retry or inferred success.
internal sealed class NIRABrowserActionJournal
{
    private readonly string _connectionString;

    public NIRABrowserActionJournal(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true, DefaultTimeout = 5
        }.ToString();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS browser_dispatch_receipts (
                id TEXT PRIMARY KEY,
                owner_key TEXT NOT NULL,
                route TEXT NOT NULL,
                origin TEXT NOT NULL,
                state TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS browser_dispatch_review
            ON browser_dispatch_receipts(owner_key, origin, state, updated_utc DESC);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    // Goal ownership survives restart. Interactive actions use a run ID so a
    // NEW explicit user request is not indefinitely blocked by an old run.
    public static string OwnerKey(NIRABrowserActionCheckpoint action) =>
        action.GoalId is Guid goal && goal != Guid.Empty
            ? $"goal:{goal:D}"
            : $"run:{action.RunId:D}";

    public void Save(NIRABrowserActionCheckpoint action)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO browser_dispatch_receipts(id,owner_key,route,origin,state,updated_utc,payload)
            VALUES($id,$owner,$route,$origin,$state,$at,$payload)
            ON CONFLICT(id) DO UPDATE SET
                state=excluded.state, updated_utc=excluded.updated_utc,
                route=excluded.route, origin=excluded.origin, payload=excluded.payload;
            """;
        command.Parameters.AddWithValue("$id", action.ActionId.ToString("D"));
        command.Parameters.AddWithValue("$owner", OwnerKey(action));
        command.Parameters.AddWithValue("$route", action.BeforeRoute);
        command.Parameters.AddWithValue("$origin", action.BeforeOrigin);
        command.Parameters.AddWithValue("$state", action.State);
        command.Parameters.AddWithValue("$at", action.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(action));
        command.ExecuteNonQuery();
    }

    public bool HasUncertainForOrigin(string ownerKey, string origin)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM browser_dispatch_receipts
            WHERE owner_key=$owner AND origin=$origin
              AND state IN ('Dispatching','OutcomeUncertain'));
            """;
        command.Parameters.AddWithValue("$owner", ownerKey);
        command.Parameters.AddWithValue("$origin", origin);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public IReadOnlyList<NIRABrowserActionCheckpoint> ReadUncertain(string ownerKey, int limit = 20)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM browser_dispatch_receipts
            WHERE owner_key=$owner AND state IN ('Dispatching','OutcomeUncertain')
            ORDER BY updated_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$owner", ownerKey);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 40));
        using SqliteDataReader reader = command.ExecuteReader();
        List<NIRABrowserActionCheckpoint> actions = [];
        while (reader.Read())
        {
            actions.Add(JsonSerializer.Deserialize<NIRABrowserActionCheckpoint>(reader.GetString(0))
                ?? throw new InvalidDataException("Invalid browser action receipt; recovery cannot proceed safely."));
        }
        return actions;
    }
}

