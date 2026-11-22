using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Atomic, version-checked persistence. The entire tool journal is stored before
/// dispatch and after observation, preventing silent operation replay on restart.
/// </summary>
public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;

    public SqliteConversationStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var builder = new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true, DefaultTimeout = 30 };
        _connectionString = builder.ToString();
        using var conn = Open();
        using var command = conn.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY NOT NULL,
                revision INTEGER NOT NULL,
                payload TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY NOT NULL,
                content TEXT NOT NULL,
                source TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_memories_created ON memories(created_utc);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<ConversationState?> LoadAsync(Guid id, CancellationToken ct)
    {
        await using var conn = Open();
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT payload, revision FROM conversations WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        ConversationState state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)))
            ?? throw new InvalidDataException("Stored conversation was empty.");
        state.Revision = reader.GetInt64(1);
        return state;
    }

    public async Task<Guid?> FindRecentUnfinishedAsync(CancellationToken ct)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 40";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ConversationState? candidate = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (candidate is not null && candidate.Status is not
                (ConversationStatus.Completed or ConversationStatus.Failed))
                return candidate.Id;
        }
        return null;
    }

    public async Task SaveAsync(ConversationState state, CancellationToken ct)
    {
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        await using var conn = Open();
        using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        long next = checked(state.Revision + 1);
        // Use an optimistic compare-and-swap to prevent two hosts overwriting each other.
        if (state.Revision == 0)
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO conversations(id, revision, payload, updated_utc)
                VALUES ($id, $next, $payload, $updated)
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE conversations SET revision=$next, payload=$payload, updated_utc=$updated
                WHERE id=$id AND revision=$current
                """;
            cmd.Parameters.AddWithValue("$current", state.Revision);
        }
        cmd.Parameters.AddWithValue("$id", state.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$next", next);
        cmd.Parameters.AddWithValue("$payload", WindowsDpapiProtector.Seal(JsonSerializer.Serialize(state)));
        cmd.Parameters.AddWithValue("$updated", state.UpdatedUtc.ToString("O"));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new AgentException("Conversation version conflict. Reload before continuing.");
        tx.Commit();
        state.Revision = next;
    }

    public async Task<string> StoreMemoryAsync(string content, string source, CancellationToken ct)
    {
        string id = Guid.NewGuid().ToString("N");
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memories (id, content, source, created_utc)
            VALUES ($id, $content, $source, $created)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$content", WindowsDpapiProtector.Seal(content));
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<(string Id, string Content)>> SearchMemoryAsync(
        string query, int limit, CancellationToken ct)
    {
        // DPAPI ciphertext is never searchable in SQLite. Search a bounded set
        // of approved memories after decrypting them under this Windows user.
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, content FROM memories ORDER BY created_utc DESC LIMIT 500";
        var matches = new List<(string Id, string Content)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            string value = WindowsDpapiProtector.Open(reader.GetString(1));
            if (value.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add((reader.GetString(0), value));
            if (matches.Count >= Math.Clamp(limit, 1, 20)) break;
        }
        return matches;
    }
}
