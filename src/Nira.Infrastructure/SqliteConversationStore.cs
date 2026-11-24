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
            CREATE TABLE IF NOT EXISTS chat_preferences (
                thread_id TEXT PRIMARY KEY NOT NULL,
                title TEXT,
                archived INTEGER NOT NULL DEFAULT 0
            );
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

    // Read-only desktop history. Decrypt using the same current-user DPAPI store.
    public async Task<IReadOnlyList<SavedConversationSummary>> ListRecentAsync(CancellationToken ct, bool includeArchived = false)
    {
        var results = new List<SavedConversationSummary>();
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 500";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            ConversationState? state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (state is not null && !string.IsNullOrWhiteSpace(state.Objective))
                results.Add(new SavedConversationSummary(state.Id, state.Objective, state.Status, state.UpdatedUtc,
                    state.ChatThreadId ?? state.Id, state.ChatThreadTitle ?? state.Objective));
        }
        // Rows are newest-first. One history card per logical chat; opening it
        // restores the linked task sequence from the encrypted store.
        await reader.DisposeAsync();
        var preferences = new Dictionary<Guid, (string? Title, bool Archived)>();
        await using var prefs = conn.CreateCommand();
        prefs.CommandText = "SELECT thread_id, title, archived FROM chat_preferences";
        await using var prefReader = await prefs.ExecuteReaderAsync(ct);
        while (await prefReader.ReadAsync(ct))
        {
            if (!Guid.TryParse(prefReader.GetString(0), out Guid id)) continue;
            preferences[id] = (prefReader.IsDBNull(1) ? null : WindowsDpapiProtector.Open(prefReader.GetString(1)),
                prefReader.GetInt64(2) != 0);
        }
        return results.GroupBy(item => item.ThreadId).Select(group => group.First()).Select(item =>
            preferences.TryGetValue(item.ThreadId, out var pref)
                ? item with { ThreadTitle = pref.Title ?? item.ThreadTitle, Archived = pref.Archived } : item)
            .Where(item => includeArchived || !item.Archived).Take(80).ToArray();
    }

    // Gallery reuses the encrypted task journal; no new database or remote access.
    public async Task<IReadOnlyList<ImageGalleryEntry>> ListGalleryAsync(CancellationToken ct)
    {
        var entries = new List<ImageGalleryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 500";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (state is null) continue;
            foreach (var entry in ImageGalleryEntry.FromTask(state).OrderByDescending(e => e.RetrievedUtc))
                if (seen.Add(entry.Sha256 + "|" + entry.Path)) entries.Add(entry);
        }
        return entries.OrderByDescending(e => e.RetrievedUtc).ToArray();
    }

    // Explicit UI operations only; not exposed to the model as execution tools.
    // Archiving is reversible and does not alter task status or any operation receipt.
    public async Task SetChatPreferencesAsync(Guid threadId, string? title, bool archived, CancellationToken ct)
    {
        if (threadId == Guid.Empty) throw new ArgumentException("Invalid chat identity.");
        if (title is not null)
        {
            title = title.Trim();
            if (title.Length is < 1 or > 160 || title.Any(char.IsControl))
                throw new ArgumentException("Use a single-line title of 1–160 characters.");
        }
        // The root must actually exist; no ghost entries and no rewriting historical tasks.
        var root = await LoadAsync(threadId, ct);
        if (root is null || (root.ChatThreadId ?? root.Id) != threadId)
            throw new AgentException("Chat thread not found.");
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_preferences(thread_id,title,archived) VALUES($id,$title,$archived)
            ON CONFLICT(thread_id) DO UPDATE SET title=COALESCE(excluded.title,chat_preferences.title), archived=excluded.archived
            """;
        cmd.Parameters.AddWithValue("$id", threadId.ToString("D"));
        cmd.Parameters.AddWithValue("$title", title is null ? (object)DBNull.Value : WindowsDpapiProtector.Seal(title));
        cmd.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SavedMemoryNote>> ListMemoryNotesAsync(CancellationToken ct)
    {
        var notes = new List<SavedMemoryNote>();
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, content, source FROM memories ORDER BY created_utc DESC LIMIT 500";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            notes.Add(new SavedMemoryNote(reader.GetString(0), WindowsDpapiProtector.Open(reader.GetString(1)), reader.GetString(2)));
        return notes;
    }

    public async Task<bool> RemoveMemoryNoteAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return false;
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<IReadOnlyList<ConversationState>> LoadThreadAsync(Guid latest, CancellationToken ct)
    {
        var chain = new List<ConversationState>();
        var visited = new HashSet<Guid>();
        Guid? next = latest;
        Guid? root = null;
        while (next is Guid id && chain.Count < 100 && visited.Add(id))
        {
            var state = await LoadAsync(id, ct);
            if (state is null) break;
            root ??= state.ChatThreadId ?? state.Id;
            if ((state.ChatThreadId ?? state.Id) != root) break;
            chain.Add(state);
            next = state.PreviousTaskId;
        }
        chain.Reverse();
        return chain;
    }

    public async Task<Guid> CreateFollowupAsync(Guid previous, CancellationToken ct)
    {
        var chain = await LoadThreadAsync(previous, ct);
        if (chain.Count == 0 || chain[^1].Status != ConversationStatus.Completed)
            throw new AgentException("Only a completed task can start a new linked chat turn.");
        var parent = chain[^1];
        var excerpts = new List<string>();
        foreach (var state in chain.TakeLast(6))
        {
            foreach (var user in state.Messages.Where(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)))
                excerpts.Add("User: " + user.Content);
            if (!string.IsNullOrWhiteSpace(state.LastAnswer)) excerpts.Add("NIRA: " + state.LastAnswer);
        }
        string context = string.Join("\n\n", excerpts);
        if (context.Length > 6000) context = context[^6000..];
        var child = new ConversationState {
            ChatThreadId = parent.ChatThreadId ?? parent.Id, PreviousTaskId = parent.Id,
            ChatThreadTitle = parent.ChatThreadTitle ?? parent.Objective, PreviousChatContext = context
        };
        await SaveAsync(child, ct);
        return child.Id;
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
            if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.Objective) && candidate.Status is not
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

public sealed record SavedConversationSummary(Guid Id, string Objective, ConversationStatus Status, DateTimeOffset UpdatedUtc, Guid ThreadId, string ThreadTitle, bool Archived = false);
public sealed record SavedMemoryNote(string Id, string Content, string Source);
